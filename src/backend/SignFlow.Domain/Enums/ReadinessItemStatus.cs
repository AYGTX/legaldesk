namespace SignFlow.Domain.Enums;

/// <summary>Where a review finding stands (R-M8-4). Only Open items count against the verdict.</summary>
public enum ReadinessItemStatus
{
    Open = 0,

    /// <summary>A review of a newer version of the contract no longer finds it. Set only by the system.</summary>
    Fixed = 1,

    /// <summary>The risk is taken on purpose, with a reason: a Critical needs a manager, a High the owner.</summary>
    Accepted = 2,

    /// <summary>A false positive, with a reason; reported to Harvey for the lawyers who keep the grid.</summary>
    NotApplicable = 3
}
