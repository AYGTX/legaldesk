using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Serialization;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Reviews;

public record ReviewRunDto(
    Guid Id,
    int VersionNumber,
    ReviewRunStatus Status,
    DateTime QueuedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    DateTime AsOfDate,
    string? GridVersion,
    int FindingCount,
    string? ErrorCode)
{
    public static ReviewRunDto From(ReviewRun r) => new(
        r.Id, r.VersionNumber, r.Status, r.CreatedAt, r.StartedAt, r.FinishedAt, r.AsOfDate, r.GridVersion,
        r.FindingCount, r.ErrorCode);
}

/// <summary>One finding with its origin (R-M8-11): rule and version, approver, detection, confidence, quotes, law.</summary>
public record ReviewItemDto(
    Guid Id,
    string? RuleKey,
    int? RuleVersion,
    string? RuleApprovedBy,
    DateTime? RuleApprovedAt,
    string Severity,
    string Title,
    string Detail,
    string? ClauseReference,
    List<string> Quotes,
    string? DetectionKind,
    double? Confidence,
    List<ReviewArticle> Articles,
    string? WhyItMatters,
    string? RecommendedAction,
    string? ProposedLanguage,
    ReadinessItemStatus Status,
    string? Reason,
    string? DecidedBy,
    DateTime? DecidedAt,
    int? FirstFoundInVersion,
    int? FixedInVersion);

/// <summary>
/// The review of a contract's current version: the latest run (whatever its outcome), the verdict and scores of the
/// last completed run, the current findings, and the findings fixed by earlier revisions. Counts rather than prose, so
/// the page words it in the reader's language.
/// </summary>
public record ReviewDto(
    Guid DocumentId,
    int DocumentVersion,
    ReadinessVerdict Verdict,
    int? ReadinessScore,
    int? RiskScore,
    DateTime? EvaluatedAt,
    int OpenCount,
    int CriticalCount,
    int HighCount,
    int HandledCount,
    ReviewRunDto? LatestRun,
    List<ReviewItemDto> Items,
    List<ReviewItemDto> FixedItems);

/// <summary>Builds <see cref="ReviewDto"/> for the query and for the commands that answer with the updated review.</summary>
public static class ReviewView
{
    /// <summary>The findings the document's last completed review reported.</summary>
    public static List<DocumentReadinessItem> Current(Document document, IEnumerable<DocumentReadinessItem> items) =>
        document.LastReviewRunId is { } runId
            ? [.. items.Where(i => i.LastSeenRunId == runId)]
            : [];

    public static async Task<ReviewDto> BuildAsync(IApplicationDbContext db, Document document, CancellationToken ct)
    {
        var items = await db.DocumentReadinessItems.AsNoTracking()
            .Where(i => i.DocumentId == document.Id)
            .ToListAsync(ct);

        var latestRun = await db.ReviewRuns.AsNoTracking()
            .Where(r => r.DocumentId == document.Id)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var current = Current(document, items);
        var fixedItems = items.Where(i => i.Status == ReadinessItemStatus.Fixed && i.RuleKey is not null).ToList();

        var deciders = current.Concat(fixedItems).Select(i => i.DecidedByUserId).OfType<Guid>().Distinct().ToList();
        var names = deciders.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(u => deciders.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var grade = ReviewGrading.Grade(current);
        var evaluated = document.LastReviewRunId is not null;

        return new ReviewDto(
            document.Id,
            document.Version,
            document.ReadinessVerdict,
            document.ReadinessScore,
            document.RiskScore,
            document.ReadinessEvaluatedAt,
            evaluated ? grade.OpenCount : 0,
            evaluated ? grade.CriticalCount : 0,
            evaluated ? grade.HighCount : 0,
            evaluated ? grade.HandledCount : 0,
            latestRun is null ? null : ReviewRunDto.From(latestRun),
            [.. current
                .OrderBy(i => i.Status == ReadinessItemStatus.Open ? 0 : 1)
                .ThenBy(i => ReviewGrading.Rank(i.Severity))
                .ThenBy(i => i.ClauseReference, StringComparer.Ordinal)
                .Select(i => Map(i, names))],
            [.. fixedItems
                .OrderByDescending(i => i.FixedInVersion)
                .ThenBy(i => ReviewGrading.Rank(i.Severity))
                .Select(i => Map(i, names))]);
    }

    public static ReviewItemDto Map(DocumentReadinessItem i, IReadOnlyDictionary<Guid, string> names) => new(
        i.Id,
        i.RuleKey,
        i.RuleVersion,
        i.RuleApprovedBy,
        i.RuleApprovedAt,
        i.Severity,
        i.Title,
        i.Detail,
        i.ClauseReference,
        Read<List<string>>(i.QuotesJson) ?? [],
        i.DetectionKind,
        i.Confidence,
        Read<List<ReviewArticle>>(i.ArticlesJson) ?? [],
        i.WhyItMatters,
        i.RecommendedAction,
        i.ProposedLanguage,
        i.Status,
        i.Reason,
        i.DecidedByUserId is { } id && names.TryGetValue(id, out var name) ? name : null,
        i.DecidedAt,
        i.FirstFoundInVersion,
        i.FixedInVersion);

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, ContractJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
