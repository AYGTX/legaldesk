namespace SignFlow.Domain.Enums;

public enum DocumentStatus
{
    Draft = 0,
    Pending = 1,
    InProgress = 2,
    Completed = 3,
    Rejected = 4,
    Expired = 5,
    Cancelled = 6,
    WaitingForOthers = 7,
    ChangesRequested = 8
}
