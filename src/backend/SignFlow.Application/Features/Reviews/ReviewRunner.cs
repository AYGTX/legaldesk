using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Reviews;

public class ReviewOptions
{
    /// <summary>A review that has not answered after this long fails with review.timeout (R-M8-9).</summary>
    public int TimeoutSeconds { get; set; } = 120;
}

public interface IReviewRunner
{
    Task RunAsync(Guid runId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs one queued review (R-M8-9): reads the contract, asks Harvey, reconciles the findings and grades the result.
/// When Harvey fails or times out, the run fails visibly and no finding changes; there is no fallback.
/// </summary>
public class ReviewRunner(
    IApplicationDbContext db,
    IContractTextProvider textProvider,
    IContractReviewService reviewService,
    INotificationHub hub,
    IAuditService auditService,
    IOptions<ReviewOptions> options,
    ILogger<ReviewRunner> logger) : IReviewRunner
{
    /// <summary>SignalR event sent to the contract's page and its owner when a run starts and when it ends.</summary>
    public const string ProgressEvent = "review-progress";

    public async Task RunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await db.ReviewRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null || run.IsFinished) return;

        var document = await db.Documents
            .Include(d => d.ReadinessItems)
            .FirstOrDefaultAsync(d => d.Id == run.DocumentId && !d.IsDeleted, cancellationToken);
        if (document is null)
        {
            run.Fail(ReviewRun.Stale, "The contract no longer exists.");
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        run.Start();
        await db.SaveChangesAsync(cancellationToken);
        await NotifyAsync(document, run, cancellationToken);

        if (document.Version != run.VersionNumber)
        {
            await FailAsync(document, run, ReviewRun.Stale,
                "The contract was revised while the review waited; review the new version.", cancellationToken);
            return;
        }

        var text = await textProvider.GetTextAsync(document, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            await FailAsync(document, run, ReviewRun.NoText,
                "The contract has no readable text, so Harvey cannot review it.", cancellationToken);
            return;
        }

        var settings = await db.OrganisationRuleSettings.AsNoTracking().ToListAsync(cancellationToken);
        var disabled = settings.Where(s => !s.Enabled).Select(s => s.RuleKey).ToList();
        var overrides = settings.Where(s => s.Enabled && s.SeverityOverride is not null)
            .ToDictionary(s => s.RuleKey, s => s.SeverityOverride!);

        ContractReview review;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            try
            {
                review = await reviewService.ReviewAsync(text, disabled, overrides, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await FailAsync(document, run, ReviewRun.TimedOut,
                    $"Harvey did not answer within {options.Value.TimeoutSeconds} seconds.", cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Harvey's review of contract {DocumentId} failed", document.Id);
                await FailAsync(document, run, ReviewRun.Unavailable,
                    "Harvey could not review the contract. Nothing was changed; try again in a few minutes.", cancellationToken);
                return;
            }
        }

        // No rule in service means nothing was checked: that must never read as a clean contract.
        if (review.RulesApplied == 0)
        {
            await FailAsync(document, run, ReviewRun.NoRules,
                "No review rule is in service: a lawyer has to approve Harvey's rules before contracts can be reviewed.", cancellationToken);
            return;
        }

        Reconcile(document, review.Findings, run);

        var grade = ReviewGrading.Grade([.. document.ReadinessItems.Where(i => i.LastSeenRunId == run.Id)]);
        document.SetReview(run.Id, grade.Verdict, grade.ReadinessScore, grade.RiskScore);
        run.Complete(review.GridVersion, review.Findings.Count);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            document.Id,
            AuditAction.ReadinessEvaluated,
            "system",
            $"Harvey review of version {run.VersionNumber}: verdict {grade.Verdict}, readiness {grade.ReadinessScore}/100, " +
            $"risk {grade.RiskScore}/100, {grade.OpenCount} open finding(s), grid {review.GridVersion ?? "-"}",
            run.RequestedByUserId,
            cancellationToken: cancellationToken);

        logger.LogInformation("Contract {DocumentId} v{Version} reviewed: {Verdict}, {Count} finding(s)",
            document.Id, run.VersionNumber, grade.Verdict, review.Findings.Count);

        await NotifyAsync(document, run, cancellationToken);
    }

    /// <summary>
    /// Matches the review's findings with the contract's items by (rule, clause). A found item keeps the owner's decision;
    /// a Fixed one that comes back is open again. An item a review of a newer version no longer finds is Fixed in that
    /// version; a review of the same version leaves every state as it is (R-M8-4).
    /// </summary>
    public void Reconcile(Document document, IReadOnlyList<ContractFinding> findings, ReviewRun run)
    {
        var byKey = document.ReadinessItems
            .Where(i => i.RuleKey is not null)
            .GroupBy(i => i.SourceKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var finding in findings)
        {
            var key = DocumentReadinessItem.KeyOf(finding.RuleKey, finding.ClauseReference);
            if (!seen.Add(key)) continue;

            if (byKey.TryGetValue(key, out var existing))
            {
                existing.Seen(finding.ToDomain(), run.Id, run.VersionNumber);
            }
            else
            {
                var item = DocumentReadinessItem.Found(document.Id, finding.ToDomain(), run.Id, run.VersionNumber);
                document.ReadinessItems.Add(item);
                db.DocumentReadinessItems.Add(item);
            }
        }

        foreach (var gone in byKey.Values.Where(i => !seen.Contains(i.SourceKey)
                                                     && i.Status != ReadinessItemStatus.Fixed
                                                     && i.LastSeenVersion < run.VersionNumber))
        {
            gone.MarkFixed(run.VersionNumber);
        }
    }

    private async Task FailAsync(Document document, ReviewRun run, string code, string message, CancellationToken ct)
    {
        run.Fail(code, message);
        await db.SaveChangesAsync(ct);

        await auditService.LogAsync(
            document.Id,
            AuditAction.ReviewFailed,
            "system",
            $"Harvey review of version {run.VersionNumber} failed ({code}); no finding was changed",
            run.RequestedByUserId,
            cancellationToken: ct);

        await NotifyAsync(document, run, ct);
    }

    private async Task NotifyAsync(Document document, ReviewRun run, CancellationToken ct)
    {
        var payload = new
        {
            DocumentId = document.Id,
            RunId = run.Id,
            run.Status,
            run.ErrorCode,
            Verdict = document.ReadinessVerdict
        };
        await hub.SendToGroupAsync($"document-{document.Id}", ProgressEvent, payload, ct);
        await hub.SendToUserAsync(document.OwnerId.ToString(), ProgressEvent, payload, ct);
    }
}
