using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Reports.Queries;

/// <summary>
/// Organization-wide reporting for administrators and managers: request volume and outcomes,
/// SLA compliance, completion times by request type and department, and revision-loop activity.
/// Unlike the dashboard/SLA queries this is NOT scoped to the current user — access is
/// enforced by the controller's role requirement.
/// </summary>
public record GetAdminReportQuery : IQuery<AdminReportDto>;

public record AdminReportDto(
    int TotalRequests,
    int ActiveRequests,
    int CompletedRequests,
    int RejectedRequests,
    int ExpiredRequests,
    int ChangesRequestedNow,
    double CompletionRatePercent,
    double AvgCompletionHours,
    double SlaCompliancePercent,
    int SlaTrackedCompleted,
    int TotalChangeRequests,
    int TotalRevisionsUploaded,
    int DocumentsWithRevisions,
    List<MonthlyVolumeDto> MonthlyVolume,
    List<TypeBreakdownDto> ByRequestType,
    List<DepartmentBreakdownDto> ByDepartment);

public record MonthlyVolumeDto(int Year, int Month, int Created, int Completed);
public record TypeBreakdownDto(string Name, string? Color, string? Icon, int Count, int Completed, double AvgCompletionHours, double SlaCompliancePercent);
public record DepartmentBreakdownDto(string Department, int Count, int Completed, double AvgCompletionHours);

public class GetAdminReportQueryHandler(IApplicationDbContext db) : IQueryHandler<GetAdminReportQuery, AdminReportDto>
{
    public async Task<AdminReportDto> HandleAsync(GetAdminReportQuery query, CancellationToken cancellationToken = default)
    {
        // One lightweight projection of every sent request; all aggregations happen in memory
        // (mirrors GetSlaMetricsQuery's approach — grouping/date math doesn't translate well to SQL).
        var docs = await db.Documents.AsNoTracking()
            .Where(d => !d.IsTemplate && d.Status != DocumentStatus.Draft)
            .Select(d => new
            {
                d.Status,
                d.CreatedAt,
                d.CompletedAt,
                d.ExpiresAt,
                TypeName = d.RequestType != null ? d.RequestType.Name : null,
                TypeColor = d.RequestType != null ? d.RequestType.Color : null,
                TypeIcon = d.RequestType != null ? d.RequestType.Icon : null,
                Department = d.Owner.Department
            })
            .ToListAsync(cancellationToken);

        var total = docs.Count;
        var active = docs.Count(d => d.Status is DocumentStatus.InProgress or DocumentStatus.Pending or DocumentStatus.WaitingForOthers);
        var completedDocs = docs.Where(d => d.Status == DocumentStatus.Completed && d.CompletedAt.HasValue).ToList();
        var rejected = docs.Count(d => d.Status == DocumentStatus.Rejected);
        var expired = docs.Count(d => d.Status == DocumentStatus.Expired);
        var changesRequestedNow = docs.Count(d => d.Status == DocumentStatus.ChangesRequested);

        var completionRate = total == 0 ? 0 : Math.Round(completedDocs.Count * 100.0 / total, 1);
        var avgCompletionHours = completedDocs.Count == 0
            ? 0
            : Math.Round(completedDocs.Average(d => (d.CompletedAt!.Value - d.CreatedAt).TotalHours), 1);

        var slaTracked = completedDocs.Where(d => d.ExpiresAt.HasValue).ToList();
        var withinSla = slaTracked.Count(d => d.CompletedAt!.Value <= d.ExpiresAt!.Value);
        var slaCompliance = slaTracked.Count == 0 ? 100.0 : Math.Round(withinSla * 100.0 / slaTracked.Count, 1);

        // Revision-loop activity (request-changes feature). Audit rows and version rows are
        // joined back to Documents so soft-deleted documents drop out via the global filter.
        var totalChangeRequests = await db.AuditLogs.AsNoTracking()
            .CountAsync(a => a.Action == AuditAction.ChangesRequested
                          && db.Documents.Any(d => d.Id == a.DocumentId), cancellationToken);
        var totalRevisions = await db.DocumentVersions.AsNoTracking()
            .CountAsync(v => db.Documents.Any(d => d.Id == v.DocumentId), cancellationToken);
        var docsWithRevisions = await db.DocumentVersions.AsNoTracking()
            .Where(v => db.Documents.Any(d => d.Id == v.DocumentId))
            .Select(v => v.DocumentId)
            .Distinct()
            .CountAsync(cancellationToken);

        // Last 6 calendar months including the current one.
        var now = DateTime.UtcNow;
        var monthly = Enumerable.Range(0, 6)
            .Select(offset => new DateTime(now.Year, now.Month, 1).AddMonths(-(5 - offset)))
            .Select(month => new MonthlyVolumeDto(
                month.Year,
                month.Month,
                docs.Count(d => d.CreatedAt.Year == month.Year && d.CreatedAt.Month == month.Month),
                completedDocs.Count(d => d.CompletedAt!.Value.Year == month.Year && d.CompletedAt.Value.Month == month.Month)))
            .ToList();

        var byType = docs
            .Where(d => d.TypeName != null)
            .GroupBy(d => d.TypeName!)
            .Select(g =>
            {
                var done = g.Where(d => d.Status == DocumentStatus.Completed && d.CompletedAt.HasValue).ToList();
                var tracked = done.Where(d => d.ExpiresAt.HasValue).ToList();
                return new TypeBreakdownDto(
                    g.Key,
                    g.First().TypeColor,
                    g.First().TypeIcon,
                    g.Count(),
                    done.Count,
                    done.Count == 0 ? 0 : Math.Round(done.Average(d => (d.CompletedAt!.Value - d.CreatedAt).TotalHours), 1),
                    tracked.Count == 0 ? 100.0 : Math.Round(tracked.Count(d => d.CompletedAt!.Value <= d.ExpiresAt!.Value) * 100.0 / tracked.Count, 1));
            })
            .OrderByDescending(t => t.Count)
            .ToList();

        var byDepartment = docs
            .GroupBy(d => string.IsNullOrWhiteSpace(d.Department) ? null : d.Department)
            .Where(g => g.Key != null)
            .Select(g =>
            {
                var done = g.Where(d => d.Status == DocumentStatus.Completed && d.CompletedAt.HasValue).ToList();
                return new DepartmentBreakdownDto(
                    g.Key!,
                    g.Count(),
                    done.Count,
                    done.Count == 0 ? 0 : Math.Round(done.Average(d => (d.CompletedAt!.Value - d.CreatedAt).TotalHours), 1));
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        return new AdminReportDto(
            total, active, completedDocs.Count, rejected, expired, changesRequestedNow,
            completionRate, avgCompletionHours, slaCompliance, slaTracked.Count,
            totalChangeRequests, totalRevisions, docsWithRevisions,
            monthly, byType, byDepartment);
    }
}
