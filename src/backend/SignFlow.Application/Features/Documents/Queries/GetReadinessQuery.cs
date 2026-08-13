using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Queries;

public record ReadinessItemDto(
    Guid Id,
    ReadinessItemCategory Category,
    string Severity,
    string SourceKey,
    string Title,
    string Detail,
    string? WhyItMatters,
    string? RecommendedAction,
    string? ProposedLanguage,
    ReadinessItemStatus Status,
    string? ResolutionNote,
    DateTime? ResolvedAt);

/// <summary>
/// The pre-flight state of a contract. Carries counts rather than a prose verdict sentence so the
/// UI renders it in the reader's language.
/// </summary>
public record ReadinessDto(
    Guid DocumentId,
    ReadinessVerdict Verdict,
    int? Score,
    DateTime? EvaluatedAt,
    int OpenCount,
    int CriticalCount,
    int HighCount,
    int HandledCount,
    int? RiskScore,
    int? ComplianceScore,
    List<ReadinessItemDto> Items)
{
    public static ReadinessDto From(Document document, ReadinessAssessment assessment, int? riskScore = null, int? complianceScore = null)
        => new(
            document.Id,
            assessment.Verdict,
            assessment.Score,
            document.ReadinessEvaluatedAt,
            assessment.OpenCount,
            assessment.CriticalCount,
            assessment.HighCount,
            assessment.HandledCount,
            riskScore,
            complianceScore,
            Order(document.ReadinessItems));

    /// <summary>Open items first, most severe first — the order the owner should work through them.</summary>
    public static List<ReadinessItemDto> Order(IEnumerable<DocumentReadinessItem> items)
        => [.. items
            .OrderBy(i => i.Status)
            .ThenBy(i => SeverityRank(i.Severity))
            .Select(Map)];

    private static int SeverityRank(string severity) => severity switch
    {
        "Critical" => 0,
        "High" => 1,
        "Medium" => 2,
        _ => 3
    };

    /// <summary>
    /// Reads one score out of a stored analysis blob. The analyses are persisted as JSON rather
    /// than columns, so there is nothing to query — and a full deserialize would be wasted work.
    /// </summary>
    public static int? ScoreFrom(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(propertyName, out var v) && v.TryGetInt32(out var score)
                ? score
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public static ReadinessItemDto Map(DocumentReadinessItem i) => new(
        i.Id, i.Category, i.Severity, i.SourceKey, i.Title, i.Detail,
        i.WhyItMatters, i.RecommendedAction, i.ProposedLanguage,
        i.Status, i.ResolutionNote, i.ResolvedAt);
}

public record GetReadinessQuery(Guid DocumentId) : IQuery<ReadinessDto>;

public class GetReadinessQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetReadinessQuery, ReadinessDto>
{
    public async Task<ReadinessDto> HandleAsync(GetReadinessQuery query, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .AsNoTracking()
            .Include(d => d.ReadinessItems)
            .FirstOrDefaultAsync(d => d.Id == query.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), query.DocumentId);

        // Readers of a contract may read its review; only owners and admins can change it.
        var isAdmin = currentUser.IsInRole("Administrator");
        var isManager = currentUser.IsInRole("Manager");
        if (!isAdmin && !isManager && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        var open = document.ReadinessItems.Where(i => i.Status == ReadinessItemStatus.Open).ToList();

        return new ReadinessDto(
            document.Id,
            document.ReadinessVerdict,
            document.ReadinessScore,
            document.ReadinessEvaluatedAt,
            open.Count,
            open.Count(i => i.Severity == "Critical"),
            open.Count(i => i.Severity == "High"),
            document.ReadinessItems.Count - open.Count,
            ReadinessDto.ScoreFrom(document.AiRiskAnalysisJson, "riskScore"),
            ReadinessDto.ScoreFrom(document.AiComplianceAnalysisJson, "complianceScore"),
            ReadinessDto.Order(document.ReadinessItems));
    }
}
