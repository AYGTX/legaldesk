using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// Where a contract sits in its term today, with every date the UI and the alert sweep need.
/// <paramref name="NoticeDeadline"/> is derived, never stored, so it cannot drift from the end date.
/// </summary>
public record ContractTermStatus(
    LifecycleStage Stage,
    DateTime? NoticeDeadline,
    int? DaysToEnd,
    int? DaysToNoticeDeadline);

/// <summary>
/// Turns the dates recorded on a contract into a lifecycle stage and the alert milestone that is
/// due today.
///
/// This is deliberately a <b>deterministic rule set over dates the owner entered</b>, not an AI
/// call and not a guess from the contract text: an alert that tells a legal team their notice
/// window has closed has to be reproducible, has to be explainable line by line if challenged, and
/// must never fire twice for the same milestone.
/// </summary>
public static class ContractTermEvaluator
{
    /// <summary>Expiry inside this many days counts as "expiring soon".</summary>
    public const int ExpiringSoonDays = 30;

    // The alert ladder, most urgent first. Stage names are persisted on the document
    // (Document.LastTermAlertStage), so renaming one re-arms that rung for every contract.
    public const string StageExpired = "Expired";
    public const string StageNoticeDeadline = "NoticeDeadline";
    public const string StageT7 = "T7";
    public const string StageT30 = "T30";
    public const string StageT60 = "T60";
    public const string StageT90 = "T90";

    /// <summary>
    /// Ladder rungs in the order they are reached as the end date approaches. Index matters: a rung
    /// is only sent if it is strictly more urgent than the last one recorded, which is what stops a
    /// term entered late from firing every milestone at once.
    /// </summary>
    private static readonly string[] LadderByUrgency =
    [
        StageT90, StageT60, StageT30, StageT7, StageNoticeDeadline, StageExpired
    ];

    /// <summary>The widest rung, used to pre-filter candidates in the database before evaluating.</summary>
    public const int WidestLadderDays = 90;

    /// <remarks>
    /// Auto-renew is deliberately not an input: it changes what the alert <i>means</i> to the reader
    /// (locked in for another term vs. simply lapsing), not which stage or milestone the dates fall
    /// into. The flag is carried on the contract and surfaced alongside the stage.
    /// </remarks>
    public static ContractTermStatus Evaluate(
        DateTime? startDate,
        DateTime? endDate,
        int? noticePeriodDays,
        DateTime asOf)
    {
        if (endDate is null)
            return new ContractTermStatus(LifecycleStage.Unknown, null, null, null);

        // Whole days only: an alert that flips at 14:37 because that is when the sweep ran would be
        // impossible to reason about. Everything is compared date-to-date.
        var today = asOf.Date;
        var end = endDate.Value.Date;

        var noticeDeadline = noticePeriodDays is > 0
            ? end.AddDays(-noticePeriodDays.Value)
            : (DateTime?)null;

        var daysToEnd = (end - today).Days;
        var daysToNoticeDeadline = noticeDeadline is not null
            ? (noticeDeadline.Value - today).Days
            : (int?)null;

        var stage = DetermineStage(startDate, today, end, daysToEnd, daysToNoticeDeadline);

        return new ContractTermStatus(stage, noticeDeadline, daysToEnd, daysToNoticeDeadline);
    }

    private static LifecycleStage DetermineStage(
        DateTime? startDate,
        DateTime today,
        DateTime end,
        int daysToEnd,
        int? daysToNoticeDeadline)
    {
        if (daysToEnd < 0)
            return LifecycleStage.Expired;

        if (startDate is not null && today < startDate.Value.Date)
            return LifecycleStage.NotStarted;

        // A closed notice window outranks a near expiry: it is the more actionable fact, and on an
        // auto-renewing contract it is the one that has already cost the organisation something.
        if (daysToNoticeDeadline is <= 0)
            return LifecycleStage.NoticeDeadlinePassed;

        return daysToEnd <= ExpiringSoonDays
            ? LifecycleStage.ExpiringSoon
            : LifecycleStage.InForce;
    }

    /// <summary>
    /// The ladder rung that should be alerted on now, or null if nothing new is due.
    /// <paramref name="lastSentStage"/> is the value stored on the document; only a strictly more
    /// urgent rung fires, so each milestone is sent exactly once and a term recorded three weeks
    /// before expiry jumps straight to its current rung instead of replaying T90/T60.
    /// </summary>
    public static string? NextAlertStage(ContractTermStatus status, string? lastSentStage)
    {
        var due = DueStage(status);
        if (due is null) return null;

        // Unrecognised stored values (e.g. a rung renamed in a later release) are treated as
        // "nothing sent yet" rather than silently suppressing every future alert.
        var lastIndex = lastSentStage is null ? -1 : Array.IndexOf(LadderByUrgency, lastSentStage);
        var dueIndex = Array.IndexOf(LadderByUrgency, due);

        return dueIndex > lastIndex ? due : null;
    }

    /// <summary>The most urgent rung the contract currently qualifies for, ignoring what was sent.</summary>
    private static string? DueStage(ContractTermStatus status)
    {
        if (status.Stage == LifecycleStage.Unknown || status.Stage == LifecycleStage.NotStarted)
            return null;

        if (status.Stage == LifecycleStage.Expired)
            return StageExpired;

        if (status.DaysToNoticeDeadline is <= 0)
            return StageNoticeDeadline;

        return status.DaysToEnd switch
        {
            <= 7 => StageT7,
            <= 30 => StageT30,
            <= 60 => StageT60,
            <= 90 => StageT90,
            _ => null
        };
    }

    /// <summary>
    /// Whether this rung warrants pulling in the legal team rather than only the contract owner.
    /// Escalation starts once the organisation has lost the ability to act unilaterally.
    /// </summary>
    public static bool IsEscalationStage(string stage) =>
        stage is StageNoticeDeadline or StageExpired;
}
