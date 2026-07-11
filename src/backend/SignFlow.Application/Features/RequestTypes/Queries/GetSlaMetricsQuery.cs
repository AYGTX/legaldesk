using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.RequestTypes.Queries;

/// <summary>SLA dashboard metrics for the current user's requests.</summary>
public record GetSlaMetricsQuery : IQuery<SlaMetricsDto>;

public record SlaMetricsDto(
    int NearingExpiration,
    int Expired,
    int CompletedWithinSla,
    int CompletedTotal,
    double SlaCompliancePercent,
    List<AvgSigningTimeDto> AverageSigningTimeByType);

public record AvgSigningTimeDto(string RequestType, double AverageHours, int Count);

public class GetSlaMetricsQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetSlaMetricsQuery, SlaMetricsDto>
{
    public async Task<SlaMetricsDto> HandleAsync(GetSlaMetricsQuery query, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();
        var now = DateTime.UtcNow;
        var soon = now.AddHours(24);

        var owned = db.Documents.AsNoTracking().Where(d => d.OwnerId == userId && !d.IsTemplate);

        // Active requests within 24h of their deadline (not yet completed/expired).
        var nearingExpiration = await owned.CountAsync(d =>
            d.Status == DocumentStatus.InProgress &&
            d.ExpiresAt != null && d.ExpiresAt > now && d.ExpiresAt <= soon, cancellationToken);

        // Expired = explicitly Expired, or still in progress past the deadline (sweep may not have run yet).
        var expired = await owned.CountAsync(d =>
            d.Status == DocumentStatus.Expired ||
            (d.Status == DocumentStatus.InProgress && d.ExpiresAt != null && d.ExpiresAt < now), cancellationToken);

        // Completed requests that had a deadline, for compliance + average signing time.
        var completed = await owned
            .Where(d => d.Status == DocumentStatus.Completed && d.CompletedAt != null)
            .Select(d => new
            {
                d.CreatedAt,
                CompletedAt = d.CompletedAt!.Value,
                d.ExpiresAt,
                RequestTypeName = d.RequestType != null ? d.RequestType.Name : null
            })
            .ToListAsync(cancellationToken);

        var withDeadline = completed.Where(c => c.ExpiresAt.HasValue).ToList();
        var completedWithinSla = withDeadline.Count(c => c.CompletedAt <= c.ExpiresAt!.Value);
        var compliance = withDeadline.Count == 0 ? 100.0 : Math.Round(completedWithinSla * 100.0 / withDeadline.Count, 1);

        var avgByType = completed
            .Where(c => c.RequestTypeName != null)
            .GroupBy(c => c.RequestTypeName!)
            .Select(g => new AvgSigningTimeDto(
                g.Key,
                Math.Round(g.Average(c => (c.CompletedAt - c.CreatedAt).TotalHours), 1),
                g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        return new SlaMetricsDto(
            nearingExpiration,
            expired,
            completedWithinSla,
            withDeadline.Count,
            compliance,
            avgByType);
    }
}
