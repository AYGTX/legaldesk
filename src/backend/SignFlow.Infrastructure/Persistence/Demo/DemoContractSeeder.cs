using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Serialization;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Persistence.Demo;

/// <summary>
/// Materialises <see cref="DemoContractCatalogue"/> into a working contract portfolio: real PDF
/// files in storage, documents with a history, validation circuits at every stage, stored risk and
/// compliance analyses, contract terms and a traceability trail.
///
/// Why this exists: LegalDesk's lifecycle report, risk map, expiry alert ladder and readiness gate
/// all read from executed contracts. On a database that has just been created there are none, so a
/// product with eleven working capabilities demonstrates none of them until somebody manually
/// drafts, sends and signs a contract in front of the audience.
///
/// Nothing here is a mock: the analyses are written through the same
/// <see cref="ReadinessEvaluator"/> and JSON contract the runtime uses, so a seeded document is
/// indistinguishable from one the application produced — including when it is re-analysed live.
/// </summary>
internal static class DemoContractSeeder
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IStorageService storage,
        IPdfService pdfService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var catalogue = DemoContractCatalogue.All;
        var titles = catalogue.Select(c => c.Title).ToList();

        // Idempotent by title: an existing demo database keeps whatever the presenter did to it.
        var alreadySeeded = await context.Documents
            .IgnoreQueryFilters()
            .AnyAsync(d => titles.Contains(d.Title), cancellationToken);
        if (alreadySeeded) return;

        var owners = new Dictionary<string, ApplicationUser>();
        foreach (var email in catalogue.Select(c => c.OwnerEmail).Distinct())
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                logger.LogWarning("Demo contract seeding skipped — owner {Email} not found", email);
                return;
            }
            owners[email] = user;
        }

        var requestTypes = await context.RequestTypes.ToDictionaryAsync(t => t.Name, cancellationToken);
        var regulations = await context.RegulatoryTexts
            .Where(r => r.Status == RegulatoryTextStatus.Published)
            .ToListAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;

        foreach (var spec in catalogue)
        {
            if (!requestTypes.TryGetValue(spec.RequestTypeName, out var requestType))
            {
                logger.LogWarning("Demo contract '{Title}' skipped — contract family {Family} not seeded",
                    spec.Title, spec.RequestTypeName);
                continue;
            }

            await SeedOneAsync(context, storage, pdfService, spec, owners[spec.OwnerEmail],
                requestType, regulations, today, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} demo contracts", catalogue.Count);
    }

    private static async Task SeedOneAsync(
        ApplicationDbContext context,
        IStorageService storage,
        IPdfService pdfService,
        DemoContract spec,
        ApplicationUser owner,
        RequestType requestType,
        IReadOnlyList<RegulatoryText> regulations,
        DateTime today,
        CancellationToken cancellationToken)
    {
        var createdAt = today.AddDays(-spec.CreatedDaysAgo).AddHours(9);

        // A real file, drawn as text so PdfPig can read it back and a live re-analysis works on the
        // same wording the reviewer sees on screen.
        var blocks = spec.Body
            .SelectMany(a => new[]
            {
                new DemoContractPdfWriter.ContractBlock(a.Heading, IsHeading: true),
                new DemoContractPdfWriter.ContractBlock(a.Text, IsHeading: false)
            })
            .ToList();

        using var pdf = DemoContractPdfWriter.Render(spec.Title, spec.Reference, blocks);
        var fileName = $"{Slug(spec.Title)}.pdf";
        var storagePath = await storage.UploadAsync(pdf, fileName, "application/pdf", "documents", cancellationToken);

        pdf.Position = 0;
        var pageCount = await pdfService.GetPageCountAsync(pdf, cancellationToken);
        pdf.Position = 0;
        var hash = await pdfService.ComputeHashAsync(pdf, cancellationToken);

        var document = Document.Create(
            spec.Title, fileName, storagePath, pdf.Length, pageCount, owner.Id,
            description: spec.Description,
            subject: spec.Title,
            message: "Merci de procéder à la validation de ce contrat.");
        document.SetHash(hash);
        document.SetWorkflowType(
            spec.Participants.GroupBy(p => p.Order).Any(g => g.Count() > 1)
                ? WorkflowType.Mixed
                : WorkflowType.Sequential);

        context.Documents.Add(document);

        // CreatedAt is stamped to "now" by the domain, deliberately — a contract cannot claim to
        // have been drafted last year. Demo data needs exactly that, so the seeder writes the
        // audit-ish timestamps through EF's property accessor rather than widening the entity API.
        Backdate(context, document, nameof(Document.CreatedAt), createdAt);
        document.SetCreatedBy(owner.Id.ToString());

        // Snapshots the SLA and derives ExpiresAt from CreatedAt, which is now backdated.
        document.ApplyRequestType(requestType.Id, requestType.SlaHours);

        var sentAt = createdAt.AddHours(4);
        var completedAt = spec.CompletedDaysAgo is { } days ? today.AddDays(-days).AddHours(16) : (DateTime?)null;

        document.SetStatus(spec.Status);
        if (spec.Status != DocumentStatus.Draft)
            Backdate(context, document, nameof(Document.SentAt), sentAt);
        if (completedAt is not null)
            Backdate(context, document, nameof(Document.CompletedAt), completedAt.Value);

        if (spec.Term is { } term)
        {
            document.SetContractTerm(
                today.AddDays(-term.StartDaysAgo),
                today.AddDays(term.EndInDays),
                term.AutoRenews,
                term.NoticePeriodDays);
        }

        if (spec.Obligation is { } obligation)
        {
            var dueDate = today.AddDays(obligation.DueInDays);
            if (obligation.Confirmed)
                document.ConfirmObligation(dueDate, obligation.Type);
            else
                document.SetObligationSuggestion(dueDate, obligation.Type, obligation.Rationale);
        }

        var analysedAt = spec.Status == DocumentStatus.Draft ? createdAt : createdAt.AddHours(1);

        ContractRiskAnalysis? risk = null;
        if (spec.Risk is { } r)
        {
            risk = new ContractRiskAnalysis(r.Score, [.. r.Findings], analysedAt, r.Profile);
            document.SetAiRiskAnalysis(JsonSerializer.Serialize(risk, ContractJson.Options));
            Backdate(context, document, nameof(Document.AiRiskAnalysisGeneratedAt), analysedAt);
        }

        var compliance = BuildCompliance(spec, requestType, regulations, analysedAt);
        if (compliance is not null)
        {
            document.SetAiComplianceAnalysis(JsonSerializer.Serialize(compliance, ContractJson.Options));
            Backdate(context, document, nameof(Document.AiComplianceAnalysisGeneratedAt), analysedAt);
        }

        if (spec.EvaluateReadiness)
            ApplyReadiness(context, document, risk, compliance, analysedAt);

        var recipients = AddParticipants(context, document, spec, sentAt, completedAt);
        AddAuditTrail(context, document, spec, owner, recipients, createdAt, sentAt, completedAt, analysedAt);
    }

    /// <summary>
    /// Builds the stored compliance analysis the way the runtime does: every regulation the
    /// document's family is subject to is answered exactly once, and the score is computed from the
    /// findings rather than asserted. Regulations the catalogue has no verdict for come back as
    /// "Unclear" — an incomplete answer must not flatter the score.
    /// </summary>
    private static ComplianceAnalysis? BuildCompliance(
        DemoContract spec,
        RequestType requestType,
        IReadOnlyList<RegulatoryText> regulations,
        DateTime analysedAt)
    {
        if (spec.Compliance is null) return null;

        // Same selection as AnalyzeComplianceCommand: everything published, minus the entries
        // scoped to a different contract family.
        var applicable = regulations
            .Where(r => r.RequestTypeId is null || r.RequestTypeId == requestType.Id)
            .ToList();
        if (applicable.Count == 0) return null;

        var findings = applicable
            .Select(regulation =>
            {
                var key = DemoContractCatalogue.NormaliseReference(regulation.Reference ?? string.Empty);
                if (!spec.Compliance.TryGetValue(key, out var verdict))
                {
                    return new ComplianceFinding(regulation.Id, regulation.Title, "Unclear",
                        "Le contrat ne comporte pas d'élément permettant de se prononcer sur cette exigence.");
                }

                return new ComplianceFinding(
                    regulation.Id,
                    regulation.Title,
                    verdict.Status,
                    verdict.Explanation,
                    // A severity chip beside "Addressed" reads as a problem — gaps only, as at runtime.
                    Severity: verdict.Status == "Gap" ? verdict.Severity : null,
                    WhyItMatters: verdict.WhyItMatters,
                    RecommendedAction: verdict.RecommendedAction);
            })
            .ToList();

        var score = (int)Math.Round(100.0 * findings.Count(f => f.Status == "Addressed") / findings.Count);
        return new ComplianceAnalysis(score, findings, analysedAt);
    }

    /// <summary>
    /// Derives the pre-flight to-do list and verdict from the stored analyses using the production
    /// evaluator, so a seeded readiness verdict is the one the application would have reached.
    /// </summary>
    private static void ApplyReadiness(
        ApplicationDbContext context,
        Document document,
        ContractRiskAnalysis? risk,
        ComplianceAnalysis? compliance,
        DateTime evaluatedAt)
    {
        var drafts = ReadinessEvaluator.BuildItems(risk, compliance);

        foreach (var draft in drafts)
        {
            var item = DocumentReadinessItem.Create(
                document.Id, draft.Category, draft.Severity, draft.SourceKey,
                draft.Title, draft.Detail, draft.WhyItMatters, draft.RecommendedAction, draft.ProposedLanguage);
            context.DocumentReadinessItems.Add(item);
            Backdate(context, item, nameof(DocumentReadinessItem.CreatedAt), evaluatedAt);
        }

        var assessment = ReadinessEvaluator.Assess(
            drafts.Count,
            [.. drafts.Select(d => d.Severity)],
            risk?.RiskScore,
            compliance?.ComplianceScore);

        document.SetReadiness(assessment.Verdict, assessment.Score);
        Backdate(context, document, nameof(Document.ReadinessEvaluatedAt), evaluatedAt);
    }

    /// <summary>
    /// Creates the participants plus the workflow and steps that mirror them, the way
    /// <c>WorkflowEngine</c> does when a request is sent: participants sharing an order form one
    /// parallel stage, and the workflow's current stage is the first one not yet finished.
    /// </summary>
    private static List<Recipient> AddParticipants(
        ApplicationDbContext context,
        Document document,
        DemoContract spec,
        DateTime sentAt,
        DateTime? completedAt)
    {
        var recipients = new List<Recipient>();
        foreach (var p in spec.Participants)
        {
            var recipient = Recipient.Create(
                document.Id, p.Name, p.Email, p.Order, p.ApprovalType, roleLabel: p.RoleLabel);

            switch (p.Status)
            {
                case WorkflowStepStatus.Completed:
                    recipient.MarkViewed();
                    recipient.MarkSigned("196.203.0.11", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)", "Tunis, Tunisie");
                    break;
                case WorkflowStepStatus.Rejected:
                    recipient.MarkViewed();
                    recipient.Reject(p.DeclineReason ?? string.Empty);
                    break;
                case WorkflowStepStatus.ChangesRequested:
                    recipient.MarkViewed();
                    recipient.RequestChanges(p.DeclineReason ?? string.Empty);
                    break;
                case WorkflowStepStatus.InProgress:
                    recipient.MarkViewed();
                    break;
            }

            context.Recipients.Add(recipient);
            recipients.Add(recipient);
        }

        if (spec.Status == DocumentStatus.Draft) return recipients;

        var workflow = Workflow.Create(document.Id, document.WorkflowType);
        context.Workflows.Add(workflow);
        workflow.Start();
        Backdate(context, workflow, nameof(Workflow.StartedAt), sentAt);

        var stages = spec.Participants
            .Select((p, index) => (Spec: p, Recipient: recipients[index]))
            .GroupBy(x => x.Spec.Order)
            .OrderBy(g => g.Key)
            .ToList();

        var currentStage = 1;
        var stageOrder = 1;
        foreach (var stage in stages)
        {
            var isParallelStage = document.WorkflowType == WorkflowType.Mixed && stage.Count() > 1;

            foreach (var (participant, recipient) in stage)
            {
                var step = WorkflowStep.Create(
                    workflow.Id, stageOrder, participant.Name, participant.ApprovalType, recipient.Id, isParallelStage);
                if (isParallelStage) step.SetParallelGroup(stageOrder);

                switch (participant.Status)
                {
                    case WorkflowStepStatus.Completed: step.Complete(); break;
                    case WorkflowStepStatus.Rejected: step.Reject(participant.DeclineReason ?? string.Empty); break;
                    case WorkflowStepStatus.ChangesRequested: step.RequestChanges(participant.DeclineReason ?? string.Empty); break;
                    case WorkflowStepStatus.InProgress: step.Start(); break;
                }

                context.WorkflowSteps.Add(step);
            }

            // The workflow sits on the earliest stage that has not finished.
            if (stage.All(x => x.Spec.Status == WorkflowStepStatus.Completed) && currentStage == stageOrder)
                currentStage = stageOrder + 1;

            stageOrder++;
        }

        while (workflow.CurrentStepOrder < currentStage) workflow.AdvanceToNextStep();

        switch (spec.Status)
        {
            case DocumentStatus.Completed:
                workflow.Complete();
                if (completedAt is not null)
                    Backdate(context, workflow, nameof(Workflow.CompletedAt), completedAt.Value);
                break;
            case DocumentStatus.Rejected:
                workflow.Reject();
                break;
            case DocumentStatus.ChangesRequested:
                workflow.RequestChanges();
                break;
        }

        return recipients;
    }

    private static void AddAuditTrail(
        ApplicationDbContext context,
        Document document,
        DemoContract spec,
        ApplicationUser owner,
        IReadOnlyList<Recipient> recipients,
        DateTime createdAt,
        DateTime sentAt,
        DateTime? completedAt,
        DateTime analysedAt)
    {
        var ownerName = $"{owner.FirstName} {owner.LastName}";

        void Log(AuditAction action, DateTime at, string description, Guid? recipientId = null,
            string? actorName = null, string? actorEmail = null)
        {
            var entry = AuditLog.Create(
                document.Id, action, "196.203.0.11", description,
                userId: recipientId is null ? owner.Id : null,
                recipientId: recipientId,
                userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
                location: "Tunis, Tunisie",
                actorName: actorName ?? ownerName,
                actorEmail: actorEmail ?? owner.Email);
            context.AuditLogs.Add(entry);
            Backdate(context, entry, nameof(AuditLog.Timestamp), at);
            Backdate(context, entry, nameof(AuditLog.CreatedAt), at);
        }

        Log(AuditAction.DocumentCreated, createdAt, $"Contrat créé — {spec.RequestTypeName}");

        if (spec.Risk is not null)
            Log(AuditAction.ContractAnalysed, analysedAt,
                $"Revue de contrat exécutée — score de risque {spec.Risk.Score}/100, {spec.Risk.Findings.Count} constat(s)");

        if (spec.Compliance is not null)
            Log(AuditAction.ComplianceAnalysed, analysedAt, "Analyse de conformité exécutée sur la veille réglementaire publiée");

        if (spec.EvaluateReadiness)
            Log(AuditAction.ReadinessEvaluated, analysedAt,
                $"Revue préalable exécutée — verdict {document.ReadinessVerdict}, score {document.ReadinessScore}/100");

        if (spec.Status == DocumentStatus.Draft) return;

        Log(AuditAction.DocumentSent, sentAt, $"Contrat transmis à {recipients.Count} intervenant(s)");
        Log(AuditAction.WorkflowStarted, sentAt, "Circuit de validation démarré");

        var at = sentAt;
        for (var i = 0; i < spec.Participants.Count; i++)
        {
            var participant = spec.Participants[i];
            var recipient = recipients[i];
            at = at.AddHours(18);

            switch (participant.Status)
            {
                case WorkflowStepStatus.Completed:
                    Log(AuditAction.DocumentSigned, at,
                        $"{participant.RoleLabel} — validation enregistrée",
                        recipient.Id, participant.Name, participant.Email);
                    break;
                case WorkflowStepStatus.Rejected:
                    Log(AuditAction.DocumentRejected, at,
                        $"{participant.RoleLabel} — refus : {participant.DeclineReason}",
                        recipient.Id, participant.Name, participant.Email);
                    break;
                case WorkflowStepStatus.ChangesRequested:
                    Log(AuditAction.ChangesRequested, at,
                        $"{participant.RoleLabel} — modifications demandées : {participant.DeclineReason}",
                        recipient.Id, participant.Name, participant.Email);
                    break;
                case WorkflowStepStatus.InProgress:
                    Log(AuditAction.DocumentViewed, at,
                        $"{participant.RoleLabel} — contrat consulté",
                        recipient.Id, participant.Name, participant.Email);
                    break;
            }
        }

        if (completedAt is not null)
        {
            Log(AuditAction.WorkflowCompleted, completedAt.Value, "Circuit de validation terminé");
            Log(AuditAction.DocumentCompleted, completedAt.Value, "Contrat exécuté et archivé");
        }
    }

    /// <summary>
    /// Writes a timestamp the domain keeps read-only. Confined to seeding: the entities are right
    /// to stamp these themselves, and nothing but demo data has a legitimate past to reconstruct.
    /// </summary>
    private static void Backdate<TEntity>(
        ApplicationDbContext context, TEntity entity, string propertyName, DateTime value)
        where TEntity : class
        => context.Entry(entity).Property(propertyName).CurrentValue = value;

    private static string Slug(string title)
    {
        var chars = title
            .Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-')
            .Select(c => c == ' ' ? '-' : char.ToLowerInvariant(c))
            .ToArray();
        return new string(chars).Trim('-');
    }
}
