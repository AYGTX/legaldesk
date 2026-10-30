using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.Reports.Queries;

/// <summary>
/// Portfolio-level risk map for administrators and managers: Harvey's current findings on every reviewed contract
/// (the findings of each contract's last completed review), counted by severity, by risk and by contract family.
/// Pure aggregation; nothing is reviewed here.
/// </summary>
public record GetRiskMapQuery : IQuery<RiskMapDto>;

public record RiskMapDto(
    int TotalAnalyzed,
    double AvgRiskScore,
    List<SeverityCountDto> BySeverity,
    List<ClauseTypeFrequencyDto> TopClauseTypes,
    List<ContractFamilyRiskDto> ByRequestType);

public record SeverityCountDto(string Severity, int Count);
public record ClauseTypeFrequencyDto(string ClauseType, int Count);
public record ContractFamilyRiskDto(string RequestTypeName, int Count, double AvgRiskScore);

public class GetRiskMapQueryHandler(IApplicationDbContext db) : IQueryHandler<GetRiskMapQuery, RiskMapDto>
{
    public async Task<RiskMapDto> HandleAsync(GetRiskMapQuery query, CancellationToken cancellationToken = default)
    {
        var reviewed = await db.Documents.AsNoTracking()
            .Where(d => d.LastReviewRunId != null && d.RiskScore != null)
            .Select(d => new
            {
                d.LastReviewRunId,
                RiskScore = d.RiskScore!.Value,
                RequestTypeName = d.RequestType != null ? d.RequestType.Name : null
            })
            .ToListAsync(cancellationToken);

        var runIds = reviewed.Select(d => d.LastReviewRunId).ToList();
        var findings = await db.DocumentReadinessItems.AsNoTracking()
            .Where(i => i.LastSeenRunId != null && runIds.Contains(i.LastSeenRunId))
            .Select(i => new { i.Severity, i.Title })
            .ToListAsync(cancellationToken);

        var total = reviewed.Count;
        var avgScore = total == 0 ? 0 : Math.Round(reviewed.Average(d => d.RiskScore), 1);

        var bySeverity = findings
            .GroupBy(f => f.Severity)
            .Select(g => new SeverityCountDto(g.Key, g.Count()))
            .OrderByDescending(s => s.Count)
            .ToList();

        var topClauseTypes = findings
            .GroupBy(f => f.Title)
            .Select(g => new ClauseTypeFrequencyDto(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .Take(10)
            .ToList();

        var byRequestType = reviewed
            .Where(d => d.RequestTypeName != null)
            .GroupBy(d => d.RequestTypeName!)
            .Select(g => new ContractFamilyRiskDto(g.Key, g.Count(), Math.Round(g.Average(d => d.RiskScore), 1)))
            .OrderByDescending(r => r.AvgRiskScore)
            .ToList();

        return new RiskMapDto(total, avgScore, bySeverity, topClauseTypes, byRequestType);
    }
}
