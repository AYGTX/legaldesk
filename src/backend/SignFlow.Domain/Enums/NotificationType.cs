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
    CommentMention = 7,
    ChangesRequested = 8,
    ObligationDue = 9,
    RegulatoryAlert = 10,
    ContractTermAlert = 11
}
