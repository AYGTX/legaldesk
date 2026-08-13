using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Serialization;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.Documents.Queries;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>
/// The pre-flight review: runs the full legal review and the regulatory check in one pass, then
/// grades whether the contract is fit to enter a validation circuit and lists the adjustments that
/// would make it so.
///
/// Findings are reconciled with what is already stored by <c>SourceKey</c>, so re-running after the
/// owner has ticked items off does not resurrect them.
/// </summary>
public record EvaluateReadinessCommand(Guid DocumentId) : ICommand<ReadinessDto>;

public class EvaluateReadinessCommandHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    IAiService aiService,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<EvaluateReadinessCommandHandler> logger) : ICommandHandler<EvaluateReadinessCommand, ReadinessDto>
{
    public async Task<ReadinessDto> HandleAsync(EvaluateReadinessCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.ReadinessItems)
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        var isAdmin = currentUser.IsInRole("Administrator");
        if (!isAdmin && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        // HTML-template documents carry no PDF to read.
        if (document.HtmlTemplateId.HasValue)
            throw new Common.Exceptions.ValidationException("Only PDF contracts can be reviewed.");

        // Same scoping rule as AnalyzeComplianceCommand: unscoped texts apply to everyone, scoped
        // ones only to their own contract family.
        var regulations = await db.RegulatoryTexts.AsNoTracking()
            .Where(r => r.Status == RegulatoryTextStatus.Published
                        && (r.RequestTypeId == null || r.RequestTypeId == document.RequestTypeId))
            .Select(r => new RegulatoryTextSummary(r.Id, r.Title, r.Keywords, r.Reference))
            .ToListAsync(cancellationToken);

        // One text extraction feeds both analyses.
        await using var pdfStream = await storage.DownloadAsync(document.StoragePath, cancellationToken);
        var text = await pdfService.ExtractTextAsync(pdfStream, cancellationToken);

        var review = await aiService.AnalyzeContractAsync(text, document.Title, DateTime.UtcNow, cancellationToken);
        var compliance = await aiService.AnalyzeContractComplianceAsync(text, document.Title, regulations, cancellationToken);

        document.SetAiRiskAnalysis(JsonSerializer.Serialize(review.Risk, ContractJson.Options));
        document.SetAiComplianceAnalysis(JsonSerializer.Serialize(compliance, ContractJson.Options));

        // A date the owner has already confirmed outranks a fresh suggestion.
        if (!document.ObligationConfirmed && review.Obligation.DueDate.HasValue)
        {
            document.SetObligationSuggestion(
                review.Obligation.DueDate,
                review.Obligation.ObligationType,
                review.Obligation.Rationale);
        }

        Reconcile(document, ReadinessEvaluator.BuildItems(review.Risk, compliance));

        var assessment = ReadinessEvaluator.Assess(
            document.ReadinessItems.Count,
            [.. document.ReadinessItems.Where(i => i.Status == ReadinessItemStatus.Open).Select(i => i.Severity)],
            review.Risk.RiskScore,
            compliance.ComplianceScore);

        document.SetReadiness(assessment.Verdict, assessment.Score);

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            document.Id,
            AuditAction.ReadinessEvaluated,
            "system",
            $"Pre-flight review run — verdict {assessment.Verdict}, readiness {assessment.Score}/100, {assessment.OpenCount} open item(s)",
            currentUser.UserId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Contract {DocumentId} readiness evaluated by {UserId}: {Verdict}, score {Score}, {Open} open item(s)",
            document.Id, currentUser.UserId, assessment.Verdict, assessment.Score, assessment.OpenCount);

        return ReadinessDto.From(document, assessment, review.Risk.RiskScore, compliance.ComplianceScore);
    }

    /// <summary>
    /// Matches freshly-found issues against the stored ones by SourceKey. Known keys keep their
    /// resolution state and only refresh their wording; keys the analysis no longer reports are
    /// closed off rather than deleted, so the history of what was flagged survives.
    /// </summary>
    private void Reconcile(Document document, List<ReadinessItemDraft> drafts)
    {
        var byKey = document.ReadinessItems.ToDictionary(i => i.SourceKey, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var draft in drafts)
        {
            if (!seen.Add(draft.SourceKey))
                continue; // two findings on the same clause type collapse into one to-do

            if (byKey.TryGetValue(draft.SourceKey, out var existing))
            {
                existing.RefreshContent(
                    draft.Category, draft.Severity, draft.Title, draft.Detail,
                    draft.WhyItMatters, draft.RecommendedAction, draft.ProposedLanguage);
            }
            else
            {
                var item = DocumentReadinessItem.Create(
                    document.Id, draft.Category, draft.Severity, draft.SourceKey,
                    draft.Title, draft.Detail,
                    draft.WhyItMatters, draft.RecommendedAction, draft.ProposedLanguage);
                document.ReadinessItems.Add(item);
                db.DocumentReadinessItems.Add(item);
            }
        }

        foreach (var stale in document.ReadinessItems
                     .Where(i => i.Status == ReadinessItemStatus.Open && !seen.Contains(i.SourceKey)))
        {
            stale.SetStatus(ReadinessItemStatus.Resolved, null, "No longer reported by the latest review");
        }
    }
}
