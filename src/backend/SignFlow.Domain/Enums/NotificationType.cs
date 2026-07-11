namespace SignFlow.Domain.Enums;

public enum NotificationType
{
    DocumentReceived = 0,
    Reminder = 1,
    Completed = 2,
    Rejected = 3,
    Expired = 4,
    WorkflowFinished = 5,
    DelegationRequest = 6,
    CommentMention = 7
}
