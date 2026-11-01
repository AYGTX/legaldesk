using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Reports.Queries;

/// <summary>
/// Portfolio view of where executed contracts sit in their own terms — the answer to "what renews
/// next quarter?". Pure aggregation over dates the owners recorded; no AI call and no stored
/// verdict, so it is always current as of the moment it is asked.
/// </summary>
public record GetContractLifecycleQuery : IQuery<ContractLifecycleDto>;

public record ContractLifecycleDto(
    int TotalTracked,
    int ExpiringIn30Days,
    int ExpiringIn60Days,
    int ExpiringIn90Days,
    int NoticeDeadlinePassed,
    List<LifecycleStageCountDto> ByStage,
    List<UpcomingContractDto> Upcoming,
    List<ContractFamilyLifecycleDto> ByRequestType);

public record LifecycleStageCountDto(string Stage, int Count);

public record UpcomingContractDto(
    Guid DocumentId,
    string Title,
    string? RequestTypeName,
    DateTime EndDate,
    DateTime? NoticeDeadline,
    bool AutoRenews,
    int DaysToEnd,
    string Stage);

public record ContractFamilyLifecycleDto(string RequestTypeName, int Count, int ExpiringIn90Days);

public class GetContractLifecycleQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetContractLifecycleQuery, ContractLifecycleDto>
{
    /// <summary>How many contracts the "next up" list shows before it stops being scannable.</summary>
    private const int UpcomingLimit = 15;

    public async Task<ContractLifecycleDto> HandleAsync(
        GetContractLifecycleQuery query, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Only executed contracts have a term worth tracking — a draft's dates are still notional.
        var tracked = await db.Documents.AsNoTracking()
            .Where(d => !d.IsDeleted
                        && d.Status == DocumentStatus.Completed
                        && d.Term != null && d.Term.CurrentEndDate != null)
            .Select(d => new
            {
                d.Id,
                d.Title,
                d.Term,
                RequestTypeName = d.RequestType != null ? d.RequestType.Name : null
            })
            .ToListAsync(cancellationToken);

        // The stage rules live in one place and are unit-tested there; applying them in memory
        // keeps this query and the alert sweep provably consistent rather than merely similar.
        var evaluated = tracked
            .Select(d => new
            {
                d.Id,
                d.Title,
                ContractEndDate = d.Term!.CurrentEndDate,
                AutoRenews = d.Term.RenewalMode == RenewalMode.Automatic,
                d.RequestTypeName,
                Status = TermLadder.Status(d.Term, now)
            })
            .ToList();

        var live = evaluated.Where(d => d.Status.Stage != LifecycleStage.Expired).ToList();

        int ExpiringWithin(int days) =>
            live.Count(d => d.Status.DaysToEnd is not null && d.Status.DaysToEnd <= days);

        var byStage = evaluated
            .GroupBy(d => d.Status.Stage)
            .Select(g => new LifecycleStageCountDto(g.Key.ToString(), g.Count()))
            .OrderBy(s => s.Stage)
            .ToList();

        var upcoming = live
            .Where(d => d.Status.DaysToEnd is not null && d.Status.DaysToEnd <= 90)
            .OrderBy(d => d.Status.DaysToEnd)
            .Take(UpcomingLimit)
            .Select(d => new UpcomingContractDto(
                d.Id,
                d.Title,
                d.RequestTypeName,
                d.ContractEndDate!.Value,
                d.Status.NoticeDeadline,
                d.AutoRenews,
                d.Status.DaysToEnd!.Value,
                d.Status.Stage.ToString()))
            .ToList();

        var byRequestType = evaluated
            .Where(d => d.RequestTypeName != null)
            .GroupBy(d => d.RequestTypeName!)
            .Select(g => new ContractFamilyLifecycleDto(
                g.Key,
                g.Count(),
                g.Count(d => d.Status.Stage != LifecycleStage.Expired
                             && d.Status.DaysToEnd is not null && d.Status.DaysToEnd <= 90)))
            .OrderByDescending(r => r.ExpiringIn90Days)
            .ThenBy(r => r.RequestTypeName)
            .ToList();

        return new ContractLifecycleDto(
            evaluated.Count,
            ExpiringWithin(30),
            ExpiringWithin(60),
            ExpiringWithin(90),
            evaluated.Count(d => d.Status.Stage == LifecycleStage.NoticeDeadlinePassed),
            byStage,
            upcoming,
            byRequestType);
    }
}
