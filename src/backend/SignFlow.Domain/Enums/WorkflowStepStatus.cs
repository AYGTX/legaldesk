namespace SignFlow.Domain.Enums;

public enum WorkflowStepStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Rejected = 3,
    Skipped = 4,
    Delegated = 5,
    Escalated = 6,
    Expired = 7
}
