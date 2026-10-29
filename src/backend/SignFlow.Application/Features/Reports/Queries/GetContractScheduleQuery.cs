using SignFlow.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Reports.Queries;

/// <summary>
/// The contract schedule (« Échéancier »): every contract with its end date, notice deadline and stage, including
/// contracts whose dates are still missing, so nothing silently drops out of the follow-up. Administrators see every
/// contract; others see their own.
/// </summary>
public record GetContractScheduleQuery : IQuery<List<ContractScheduleRowDto>>;

/// <param name="Stage">A <see cref="LifecycleStage"/> name, or "MissingDates" when no end date is recorded.</param>
/// <param name="TermConfirmed">False when the dates were read from the contract by the AI assistant and nobody has
/// confirmed them yet.</param>
public record ContractScheduleRowDto(
    Guid DocumentId,
    string Title,
    string Status,
    string? RequestTypeName,
    DateTime? StartDate,
    DateTime? EndDate,
    DateTime? NoticeDeadline,
    int? DaysToEnd,
    int? DaysToNoticeDeadline,
    bool AutoRenews,
    int? NoticePeriodDays,
    bool TermConfirmed,
    string Stage,
    DateTime CreatedAt);

public class GetContractScheduleQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IQueryHandler<GetContractScheduleQuery, List<ContractScheduleRowDto>>
{
    public const string MissingDates = "MissingDates";

    public async Task<List<ContractScheduleRowDto>> HandleAsync(GetContractScheduleQuery query, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var isAdmin = currentUser.Can(Permissions.ManageAllContracts);
        var userId = currentUser.UserId;

        var rows = await db.Documents.AsNoTracking()
            .Where(d => !d.IsDeleted && !d.IsTemplate && (isAdmin || d.OwnerId == userId))
            .Select(d => new
            {
                d.Id, d.Title, d.Status, d.ContractStartDate, d.ContractEndDate, d.AutoRenews, d.NoticePeriodDays,
                d.TermConfirmed, d.CreatedAt,
                RequestTypeName = d.RequestType != null ? d.RequestType.Name : null
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(d =>
            {
                var s = ContractTermEvaluator.Evaluate(d.ContractStartDate, d.ContractEndDate, d.NoticePeriodDays, now);
                return new ContractScheduleRowDto(
                    d.Id, d.Title, d.Status.ToString(), d.RequestTypeName, d.ContractStartDate, d.ContractEndDate,
                    s.NoticeDeadline, s.DaysToEnd, s.DaysToNoticeDeadline, d.AutoRenews, d.NoticePeriodDays,
                    d.TermConfirmed, d.ContractEndDate is null ? MissingDates : s.Stage.ToString(), d.CreatedAt);
            })
            // Most urgent first: a missed notice deadline, then the nearest end dates; contracts without dates last.
            .OrderBy(r => r.Stage == MissingDates ? 1 : 0)
            .ThenBy(r => r.Stage == nameof(LifecycleStage.NoticeDeadlinePassed) ? 0 : 1)
            .ThenBy(r => r.DaysToEnd ?? int.MaxValue)
            .ToList();
    }
}
