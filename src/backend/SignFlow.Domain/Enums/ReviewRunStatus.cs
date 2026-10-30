namespace SignFlow.Domain.Enums;

public enum ReviewRunStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,

    /// <summary>Harvey could not be reached, timed out, or the contract had no readable text. Nothing was changed.</summary>
    Failed = 3
}
