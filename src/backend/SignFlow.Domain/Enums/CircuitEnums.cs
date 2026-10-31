namespace SignFlow.Domain.Enums;

/// <summary>When a stage of the circuit is done (R-M4-2).</summary>
public enum StageRule
{
    /// <summary>Every step of the stage.</summary>
    All = 0,

    /// <summary>The first step that completes; the others are closed as Skipped.</summary>
    Any = 1
}

/// <summary>How a round of the circuit ended (R-M7-2).</summary>
public enum RoundOutcome
{
    Open = 0,
    Completed = 1,
    Declined = 2,
    ChangesRequested = 3,
    /// <summary>A revision replaced the version before the round ended.</summary>
    Superseded = 4
}
