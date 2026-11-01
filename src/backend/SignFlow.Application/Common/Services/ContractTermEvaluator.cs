using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// Where a contract sits in its term today, with every date the UI and the reports need.
/// <paramref name="NoticeDeadline"/> is derived from the term, never stored, so it cannot drift from the end date.
/// </summary>
public record ContractTermStatus(
    LifecycleStage Stage,
    DateTime? NoticeDeadline,
    int? DaysToEnd,
    int? DaysToNoticeDeadline);

/// <summary>
/// Turns the dates recorded on a contract into a lifecycle stage. A deterministic rule over dates a person confirmed,
/// never a guess from the text. The alerts themselves are <see cref="TermLadder"/>.
/// </summary>
public static class ContractTermEvaluator
{
    /// <summary>Expiry inside this many days counts as "expiring soon".</summary>
    public const int ExpiringSoonDays = 30;

    public static ContractTermStatus Evaluate(DateTime? startDate, DateTime? endDate, DateTime? noticeDeadline, DateTime asOf)
    {
        if (endDate is null)
            return new ContractTermStatus(LifecycleStage.Unknown, null, null, null);

        // Whole days only: everything is compared date to date.
        var today = asOf.Date;
        var end = endDate.Value.Date;
        var deadline = noticeDeadline?.Date;

        var daysToEnd = (end - today).Days;
        var daysToNoticeDeadline = deadline is not null ? (deadline.Value - today).Days : (int?)null;

        LifecycleStage stage;
        if (daysToEnd < 0) stage = LifecycleStage.Expired;
        else if (startDate is not null && today < startDate.Value.Date) stage = LifecycleStage.NotStarted;
        // A closed notice window outranks a near expiry: it is the more actionable fact.
        else if (daysToNoticeDeadline is <= 0) stage = LifecycleStage.NoticeDeadlinePassed;
        else stage = daysToEnd <= ExpiringSoonDays ? LifecycleStage.ExpiringSoon : LifecycleStage.InForce;

        return new ContractTermStatus(stage, deadline, daysToEnd, daysToNoticeDeadline);
    }
}
