namespace SignFlow.Domain.Enums;

/// <summary>Where a readiness to-do item stands. Only Open items count against the verdict.</summary>
public enum ReadinessItemStatus
{
    Open = 0,

    /// <summary>The owner made the adjustment (or a re-run no longer finds the issue).</summary>
    Resolved = 1,

    /// <summary>The owner judged the finding not applicable to this contract.</summary>
    Dismissed = 2
}
