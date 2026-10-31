using SignFlow.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Services.Background;

/// <summary>
/// Runs a contract's circuit (M4). Each send opens a round (R-M7-2) whose steps are built from the participants: one
/// stage per order, done when all of its steps are, or the first one under the "Any" rule (R-M4-2). A team or role step
/// is written to all of its members, and any one acts for it (R-M4-3). Opening a stage sets each step's due date in
/// working days (R-M4-10), and the follow-up sweep reminds and escalates (R-M4-6). A participant can hand their step
/// to someone else, and someone away hands it to the stand-in they named (R-M4-8).
/// </summary>
public class WorkflowEngine(
    IApplicationDbContext db,
    IEmailService emailService,
    INotificationHub hub,
    INotificationService notifications,
    ILogger<WorkflowEngine> logger,
    SignFlow.Application.Features.Documents.Commands.IDocumentJobs? documentJobs = null,
    ICircuitDirectory? directory = null) : IWorkflowEngine
{
    private static readonly WorkflowStepStatus[] Open = [WorkflowStepStatus.Pending, WorkflowStepStatus.InProgress];

    public async Task StartAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.Recipients)
            .Include(d => d.Workflow)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document {documentId} not found.");

        document.SetStatus(DocumentStatus.InProgress);

        var workflow = document.Workflow;
        if (workflow is null)
        {
            workflow = Workflow.Create(documentId, document.WorkflowType);
            db.Workflows.Add(workflow);
        }

        var round = await CurrentRoundAsync(documentId, cancellationToken);
        if (round is null || !round.IsOpen)
        {
            var first = round is null;
            round = ContractRound.Open(documentId, (round?.Number ?? 0) + 1, document.Version);
            db.ContractRounds.Add(round);
            if (!first) workflow.NewRound();
            workflow.SetType(document.WorkflowType);
            BuildSteps(workflow, round, document.WorkflowType, document.Recipients);

            // Viewers are not part of the circuit: they get their link once, with the first round.
            if (first)
                await NotifyViewersAsync(document, cancellationToken);
        }

        workflow.Start();
        await db.SaveChangesAsync(cancellationToken);

        await ActivateNextStageAsync(documentId, cancellationToken);
    }

    private void BuildSteps(Workflow workflow, ContractRound round, WorkflowType type, IEnumerable<Recipient> recipients)
    {
        var participants = recipients
            .Where(r => r.ApprovalType != ApprovalType.ViewOnly && r.RetiredAt is null && r.DelegatedFromRecipientId is null)
            .OrderBy(r => r.Order)
            .ToList();

        // Sequential and Mixed: one stage per order, the participants sharing an order act together. Parallel: one stage.
        var stages = type == WorkflowType.Parallel
            ? participants.Count == 0 ? [] : [participants]
            : participants.GroupBy(r => r.Order).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();

        var stageOrder = 1;
        foreach (var stage in stages)
        {
            var together = stage.Count > 1;
            var rule = stage.Any(r => r.StageRule == StageRule.Any) ? StageRule.Any : StageRule.All;
            foreach (var r in stage)
            {
                var step = WorkflowStep.Create(workflow.Id, stageOrder, r.Name, r.ApprovalType, r.Id, together, round.Id, rule);
                if (together) step.SetParallelGroup(stageOrder);
                db.WorkflowSteps.Add(step);
            }
            stageOrder++;
        }
    }

    private async Task NotifyViewersAsync(Document document, CancellationToken cancellationToken)
    {
        foreach (var viewer in document.Recipients.Where(r => r.ApprovalType == ApprovalType.ViewOnly && r.RetiredAt is null))
        {
            // The link's token is in this e-mail only; the database keeps its hash (R-M5-1).
            var viewerToken = SecureTokens.Generate();
            viewer.IssueLink(SecureTokens.Hash(viewerToken));
            await emailService.SendTemplateAsync(
                viewer.Email,
                "DocumentReceived",
                new { viewer.Name, DocumentTitle = document.Title, SigningLink = $"/sign/{viewerToken}" },
                "A contract was shared with you",
                cancellationToken);

            await notifications.NotifyByEmailAsync(
                viewer.Email,
                NotificationType.DocumentReceived,
                "A contract was shared with you",
                $"You have view access to the contract \"{document.Title}\".",
                document.Id,
                cancellationToken);
        }
    }

    public async Task AdvanceAsync(Guid documentId, Guid recipientId, CancellationToken cancellationToken = default)
    {
        var recipient = await db.Recipients.FindAsync([recipientId], cancellationToken);
        if (recipient is null) return;

        recipient.MarkSigned("system", "workflow-engine");

        var round = await CurrentRoundAsync(documentId, cancellationToken);
        var steps = round is null ? [] : await StepsOfAsync(round.Id, cancellationToken);
        var step = steps.FirstOrDefault(s => s.RecipientId == recipientId && Open.Contains(s.Status));
        if (step is not null)
        {
            step.Complete();
            step.ActedBy(recipient.ActedByUserId);

            // Under "Any", the first answer closes the stage: the others no longer need to act (R-M4-2).
            if (step.StageRule == StageRule.Any)
            {
                foreach (var other in steps.Where(s => s.Order == step.Order && s.Id != step.Id && Open.Contains(s.Status)))
                {
                    other.Skip();
                    var otherRecipient = await db.Recipients.FindAsync([other.RecipientId], cancellationToken);
                    otherRecipient?.Skip();
                }
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        // Notify the owner of signing progress (role-aware wording)
        var document = await db.Documents.FindAsync([documentId], cancellationToken);
        if (document is not null)
        {
            var verb = recipient.ApprovalType switch
            {
                ApprovalType.Review => "reviewed and approved",
                ApprovalType.Approval => "approved",
                _ => "signed"
            };
            await notifications.NotifyUserAsync(
                document.OwnerId,
                NotificationType.Completed,
                recipient.ApprovalType is ApprovalType.Review or ApprovalType.Approval
                    ? "A participant approved"
                    : "A participant signed",
                $"{recipient.Name} {verb} \"{document.Title}\".",
                documentId,
                cancellationToken);
        }

        if (await IsCompleteAsync(documentId, cancellationToken))
        {
            await CompleteDocumentAsync(documentId, cancellationToken);
        }
        else if (step is not null && !steps.Any(s => s.Order == step.Order && Open.Contains(s.Status)))
        {
            var workflow = await db.Workflows.FirstOrDefaultAsync(w => w.DocumentId == documentId, cancellationToken);
            workflow?.AdvanceToNextStep();
            await db.SaveChangesAsync(cancellationToken);
            await ActivateNextStageAsync(documentId, cancellationToken);
        }
    }

    public async Task RejectAsync(Guid documentId, Guid recipientId, string reason, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.Recipients)
            .Include(d => d.Workflow)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null) return;

        var recipient = document.Recipients.FirstOrDefault(r => r.Id == recipientId);
        recipient?.Reject(reason);
        var round = await CurrentRoundAsync(documentId, cancellationToken);
        if (round is not null)
        {
            (await StepsOfAsync(round.Id, cancellationToken)).FirstOrDefault(s => s.RecipientId == recipientId && Open.Contains(s.Status))?.Reject(reason);
            round.Close(RoundOutcome.Declined);
        }
        document.Workflow?.Reject();
        document.SetStatus(DocumentStatus.Rejected);

        await db.SaveChangesAsync(cancellationToken);

        // Notify document owner
        var owner = await db.Users.FindAsync([document.OwnerId], cancellationToken);
        if (owner is not null)
        {
            await emailService.SendTemplateAsync(owner.Email!, "Rejected", new { document.Title, Reason = reason, RecipientName = recipient?.Name }, cancellationToken: cancellationToken);
        }

        await notifications.NotifyUserAsync(
            document.OwnerId,
            NotificationType.Rejected,
            "Contract declined",
            $"{recipient?.Name ?? "A participant"} declined to sign the contract \"{document.Title}\"" + (string.IsNullOrWhiteSpace(reason) ? "." : $": {reason}"),
            documentId,
            cancellationToken);

        logger.LogInformation("Document {DocumentId} rejected by recipient {RecipientId}", documentId, recipientId);
    }

    /// <summary>
    /// Non-terminal counterpart of RejectAsync: the round ends in ChangesRequested and the contract waits for the
    /// owner's revision, which opens the next round.
    /// </summary>
    public async Task RequestChangesAsync(Guid documentId, Guid recipientId, string reason, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.Recipients)
            .Include(d => d.Workflow)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null) return;

        var recipient = document.Recipients.FirstOrDefault(r => r.Id == recipientId);
        recipient?.RequestChanges(reason);

        var round = await CurrentRoundAsync(documentId, cancellationToken);
        if (round is not null)
        {
            (await StepsOfAsync(round.Id, cancellationToken)).FirstOrDefault(s => s.RecipientId == recipientId && Open.Contains(s.Status))?.RequestChanges(reason);
            round.Close(RoundOutcome.ChangesRequested);
        }
        document.Workflow?.RequestChanges();
        document.SetStatus(DocumentStatus.ChangesRequested);

        await db.SaveChangesAsync(cancellationToken);

        var owner = await db.Users.FindAsync([document.OwnerId], cancellationToken);
        if (owner is not null)
        {
            await emailService.SendTemplateAsync(
                owner.Email!,
                "ChangesRequested",
                new { document.Title, Reason = reason, RecipientName = recipient?.Name },
                "Changes requested on your contract",
                cancellationToken);
        }

        await notifications.NotifyUserAsync(
            document.OwnerId,
            NotificationType.ChangesRequested,
            "Changes requested",
            $"{recipient?.Name ?? "A participant"} requested changes to the contract \"{document.Title}\": {reason}",
            documentId,
            cancellationToken);

        logger.LogInformation("Changes requested on document {DocumentId} by recipient {RecipientId}", documentId, recipientId);
    }

    public async Task CheckExpirationAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.FindAsync([documentId], cancellationToken);
        if (document is null || !document.IsExpired()) return;

        if (document.Status == DocumentStatus.InProgress)
        {
            document.SetStatus(DocumentStatus.Expired);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Document {DocumentId} expired", documentId);
        }
    }

    /// <summary>The latest round has steps and none is left to act.</summary>
    public async Task<bool> IsCompleteAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var round = await CurrentRoundAsync(documentId, cancellationToken);
        if (round is null) return false;
        if (round.Outcome == RoundOutcome.Completed) return true;
        if (!round.IsOpen) return false;

        var steps = await StepsOfAsync(round.Id, cancellationToken);
        return steps.Count > 0 && !steps.Any(s => Open.Contains(s.Status));
    }

    public async Task<Recipient?> GetCurrentRecipientAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var round = await CurrentRoundAsync(documentId, cancellationToken);
        if (round is null || !round.IsOpen) return null;
        var recipientId = await db.WorkflowSteps
            .Where(s => s.RoundId == round.Id && s.Status == WorkflowStepStatus.InProgress)
            .OrderBy(s => s.Order)
            .Select(s => (Guid?)s.RecipientId)
            .FirstOrDefaultAsync(cancellationToken);
        return recipientId is null ? null : await db.Recipients.FindAsync([recipientId.Value], cancellationToken);
    }

    public async Task<bool> IsTurnAsync(Guid recipientId, CancellationToken cancellationToken = default) =>
        await db.WorkflowSteps.AnyAsync(s => s.RecipientId == recipientId && s.Status == WorkflowStepStatus.InProgress &&
            db.ContractRounds.Any(r => r.Id == s.RoundId && r.Outcome == RoundOutcome.Open), cancellationToken);

    public async Task<Recipient> DelegateAsync(Guid documentId, Guid recipientId, string name, string email, string note,
        CancellationToken cancellationToken = default)
    {
        var round = await CurrentRoundAsync(documentId, cancellationToken);
        var step = round is { IsOpen: true }
            ? (await StepsOfAsync(round.Id, cancellationToken)).FirstOrDefault(s => s.RecipientId == recipientId && s.Status == WorkflowStepStatus.InProgress)
            : null;
        if (step is null)
            throw new ConflictException("step.not_open", "Only a step that is open can be handed to someone else.");

        var document = await db.Documents.FindAsync([documentId], cancellationToken)
            ?? throw new NotFoundException(nameof(Document), documentId);
        var recipient = await db.Recipients.FindAsync([recipientId], cancellationToken)
            ?? throw new NotFoundException(nameof(Recipient), recipientId);
        if (string.Equals(recipient.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("delegate.self", "The step cannot be handed to the same person.");

        var stand = await HandOverAsync(document, step, recipient, name, email, note, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return stand;
    }

    /// <summary>The stand-in becomes a participant of the same stage, with the step's due date and the same fields.</summary>
    private async Task<Recipient> HandOverAsync(Document document, WorkflowStep step, Recipient recipient, string name, string email,
        string note, CancellationToken cancellationToken)
    {
        var stand = Recipient.Create(document.Id, name.Trim(), email.Trim(), recipient.Order, recipient.ApprovalType,
            company: null, roleLabel: recipient.RoleLabel);
        stand.StandsInFor(recipient.Id);
        stand.SetCircuit(recipient.StageRule, null, null, recipient.DueInWorkingDays, recipient.ReminderEveryDays,
            recipient.EscalateAfterDays, recipient.EscalationEmail);
        db.Recipients.Add(stand);

        recipient.DelegateTo(stand.Id, note);
        step.Delegated();

        var next = WorkflowStep.Create(step.WorkflowId, step.Order, stand.Name, step.ApprovalType, stand.Id, step.IsParallel,
            step.RoundId, step.StageRule, step.Id);
        if (step.ParallelGroupId is { } group) next.SetParallelGroup(group);
        next.Open(DateTime.UtcNow, step.DueAt, stand.ReminderEveryDays, stand.EscalateAfterDays, stand.EscalationEmail);
        db.WorkflowSteps.Add(next);

        // The fields the participant was to fill or sign are now the stand-in's.
        var fields = await db.SignatureFields.Where(f => f.RecipientId == recipient.Id && !f.IsSigned).ToListAsync(cancellationToken);
        foreach (var field in fields)
            field.AssignToRecipient(stand.Id);

        stand.MarkViewed();
        await NotifyTurnAsync(document, stand, TurnNotice.Delegated, recipient.Name, cancellationToken);

        await notifications.NotifyUserAsync(
            document.OwnerId,
            NotificationType.DelegationRequest,
            "Step handed over",
            $"{recipient.Name} handed their step on \"{document.Title}\" to {stand.Name}: {note}",
            document.Id,
            cancellationToken);

        logger.LogInformation("Step {StepId} of document {DocumentId} handed from {From} to {To}", step.Id, document.Id, recipient.Id, stand.Id);
        return stand;
    }

    public async Task<int> FollowUpAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var due = await db.WorkflowSteps
            .Where(s => s.Status == WorkflowStepStatus.InProgress
                        && ((s.NextReminderAt != null && s.NextReminderAt <= utcNow)
                            || (s.EscalationDays != null && s.EscalatedAt == null && s.OpenedAt != null)))
            .Where(s => db.ContractRounds.Any(r => r.Id == s.RoundId && r.Outcome == RoundOutcome.Open))
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var step in due)
        {
            var recipient = await db.Recipients.FindAsync([step.RecipientId], cancellationToken);
            var document = recipient is null ? null : await db.Documents.Include(d => d.Owner)
                .FirstOrDefaultAsync(d => d.Id == recipient.DocumentId, cancellationToken);
            if (recipient is null || document is null || document.Status != DocumentStatus.InProgress) continue;

            if (step.NextReminderAt is { } at && at <= utcNow)
            {
                await NotifyTurnAsync(document, recipient, TurnNotice.Reminder, null, cancellationToken);
                step.ReminderSent(utcNow);
                sent++;
            }

            if (step.EscalationDays is { } days && step.EscalatedAt is null && step.OpenedAt is { } opened && opened.AddDays(days) <= utcNow)
            {
                var to = step.EscalationEmail ?? document.Owner.Email!;
                await emailService.SendTemplateAsync(
                    to,
                    "StepEscalated",
                    new { DocumentTitle = document.Title, Participant = recipient.Name, OpenedAt = opened, Days = days },
                    $"Waiting for {recipient.Name} for {days} day(s): \"{document.Title}\"",
                    cancellationToken);
                await notifications.NotifyUserAsync(
                    document.OwnerId,
                    NotificationType.Reminder,
                    "A step is late",
                    $"{recipient.Name} has not acted on \"{document.Title}\" for {days} day(s).",
                    document.Id,
                    cancellationToken);
                step.Escalated(utcNow);
                sent++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return sent;
    }

    private async Task ActivateNextStageAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var round = await CurrentRoundAsync(documentId, cancellationToken);
        if (round is not { IsOpen: true }) return;

        var steps = await StepsOfAsync(round.Id, cancellationToken);
        // A stage still running keeps the next one closed.
        if (steps.Any(s => s.Status == WorkflowStepStatus.InProgress)) return;

        var next = steps.Where(s => s.Status == WorkflowStepStatus.Pending).OrderBy(s => s.Order).FirstOrDefault();
        if (next is null) return;

        var document = await db.Documents.FindAsync([documentId], cancellationToken);
        if (document is null) return;
        var organisation = await db.Organisations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == document.OrganisationId, cancellationToken);
        var holidays = await db.OrganisationHolidays.AsNoTracking().ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var step in steps.Where(s => s.Order == next.Order && s.Status == WorkflowStepStatus.Pending).ToList())
        {
            var recipient = await db.Recipients.FindAsync([step.RecipientId], cancellationToken);
            if (recipient is null) continue;

            step.Open(now, WorkingCalendar.DueAt(now, recipient.DueInWorkingDays, organisation?.TimeZoneId, holidays),
                recipient.ReminderEveryDays, recipient.EscalateAfterDays, recipient.EscalationEmail);
            recipient.MarkViewed();

            // Someone away who named a stand-in: the step goes straight to them (R-M1-8).
            if (!recipient.IsPool && directory is not null
                && await directory.AbsenceDelegateAsync(recipient.Email, now, cancellationToken) is { } standIn
                && !string.Equals(standIn.Email, recipient.Email, StringComparison.OrdinalIgnoreCase))
            {
                await HandOverAsync(document, step, recipient, standIn.FullName, standIn.Email!,
                    $"{recipient.Name} is away; {standIn.FullName} acts for them.", cancellationToken);
                continue;
            }

            await NotifyTurnAsync(document, recipient, TurnNotice.Turn, null, cancellationToken);
            logger.LogInformation("Activated signing for recipient {RecipientId} on document {DocumentId}", step.RecipientId, documentId);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private enum TurnNotice { Turn, Reminder, Delegated }

    /// <summary>
    /// Tells the participant it is their turn. A named participant gets a fresh link (the token is in this e-mail only,
    /// R-M5-1); the members of a team or role step get the contract in their LegalDesk account, where they act signed in.
    /// </summary>
    private async Task NotifyTurnAsync(Document document, Recipient recipient, TurnNotice notice, string? from, CancellationToken cancellationToken)
    {
        var docTitle = document.Title;
        var (subject, title, body) = notice switch
        {
            TurnNotice.Reminder => ("Reminder: a contract is waiting for you", "Reminder",
                $"The contract \"{docTitle}\" is still waiting for you."),
            TurnNotice.Delegated => ($"{from} asked you to act on a contract", "A step was handed to you",
                $"{from} handed you their step on the contract \"{docTitle}\"."),
            _ => recipient.ApprovalType switch
            {
                ApprovalType.Review => ("Please review this contract", "It's your turn to review",
                    $"The contract \"{docTitle}\" is waiting for your review."),
                ApprovalType.Approval => ("Your approval is requested", "It's your turn to approve",
                    $"The contract \"{docTitle}\" is waiting for your approval."),
                _ => ("Please sign this contract", "It's your turn to sign",
                    $"The contract \"{docTitle}\" is waiting for your signature.")
            }
        };
        var template = notice == TurnNotice.Reminder ? "Reminder" : "DocumentReceived";

        if (recipient.IsPool)
        {
            var members = directory is null ? [] : await directory.PoolMembersAsync(recipient.PoolTeamId, recipient.PoolRole, cancellationToken);
            if (members.Count == 0)
                logger.LogWarning("Team or role step {RecipientId} of document {DocumentId} has no active member", recipient.Id, document.Id);
            foreach (var member in members)
            {
                await emailService.SendTemplateAsync(
                    member.Email!,
                    template,
                    new { Name = member.FullName, DocumentTitle = docTitle, Group = recipient.Name, SigningLink = $"/sign/session?r={recipient.Id}" },
                    $"{subject} ({recipient.Name})",
                    cancellationToken);
                await notifications.NotifyUserAsync(member.Id, notice == TurnNotice.Reminder ? NotificationType.Reminder : NotificationType.DocumentReceived,
                    title, $"{body} ({recipient.Name})", document.Id, cancellationToken);
            }
            return;
        }

        var token = SecureTokens.Generate();
        recipient.IssueLink(SecureTokens.Hash(token));
        await emailService.SendTemplateAsync(
            recipient.Email,
            template,
            new { recipient.Name, DocumentTitle = docTitle, SigningLink = $"/sign/{token}" },
            subject,
            cancellationToken);

        await notifications.NotifyByEmailAsync(
            recipient.Email,
            notice == TurnNotice.Reminder ? NotificationType.Reminder : NotificationType.DocumentReceived,
            title,
            body,
            document.Id,
            cancellationToken);
    }

    private Task<ContractRound?> CurrentRoundAsync(Guid documentId, CancellationToken cancellationToken) =>
        db.ContractRounds.Where(r => r.DocumentId == documentId).OrderByDescending(r => r.Number).FirstOrDefaultAsync(cancellationToken);

    private Task<List<WorkflowStep>> StepsOfAsync(Guid roundId, CancellationToken cancellationToken) =>
        db.WorkflowSteps.Where(s => s.RoundId == roundId).OrderBy(s => s.Order).ToListAsync(cancellationToken);

    private async Task CompleteDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await db.Documents
            .Include(d => d.Workflow)
            .Include(d => d.Owner)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null) return;

        document.SetStatus(DocumentStatus.Completed);
        document.Workflow?.Complete();
        (await CurrentRoundAsync(documentId, cancellationToken))?.Close(RoundOutcome.Completed);
        await db.SaveChangesAsync(cancellationToken);

        // Evidence page and seal, in the background (R-M5-8).
        documentJobs?.EnqueueSeal(document.Id, document.OrganisationId);

        await emailService.SendTemplateAsync(
            document.Owner.Email!,
            "Completed",
            new { document.Title },
            "Contract signed by all parties",
            cancellationToken);

        await hub.SendToGroupAsync(
            $"document-{documentId}",
            "document-completed",
            new { DocumentId = documentId },
            cancellationToken);

        await notifications.NotifyUserAsync(
            document.OwnerId,
            NotificationType.WorkflowFinished,
            "Contract completed",
            $"The contract \"{document.Title}\" has been signed by all parties.",
            documentId,
            cancellationToken);

        logger.LogInformation("Document {DocumentId} completed", documentId);
    }
}
