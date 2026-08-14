namespace SignFlow.Domain.Enums;

/// <summary>
/// Where an executed contract sits in its own term, relative to today. Derived from the recorded
/// start/end dates and notice period — never stored, so it cannot go stale. See ContractTermEvaluator.
/// </summary>
public enum LifecycleStage
{
    /// <summary>No end date recorded, so the contract's lifecycle is not being tracked.</summary>
    Unknown = 0,

    /// <summary>The term has been recorded but has not begun yet.</summary>
    NotStarted = 1,

    /// <summary>In force, with both the notice deadline and expiry comfortably ahead.</summary>
    InForce = 2,

    /// <summary>
    /// Still in force, but the last date to give notice has passed. If the contract auto-renews,
    /// the window to walk away has already closed. This is the stage legal teams actually miss,
    /// so it takes precedence over <see cref="ExpiringSoon"/> when both would apply.
    /// </summary>
    NoticeDeadlinePassed = 3,

    /// <summary>Expiry is within 30 days and the notice deadline (if any) has not passed.</summary>
    ExpiringSoon = 4,

    /// <summary>The end date has passed.</summary>
    Expired = 5
}
