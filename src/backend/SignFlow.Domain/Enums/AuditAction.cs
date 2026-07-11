namespace SignFlow.Domain.Enums;

public enum AuditAction
{
    DocumentCreated = 0,
    DocumentViewed = 1,
    DocumentSent = 2,
    DocumentSigned = 3,
    DocumentRejected = 4,
    DocumentCompleted = 5,
    DocumentExpired = 6,
    DocumentCancelled = 7,
    DocumentDownloaded = 8,
    FieldSigned = 9,
    FieldCleared = 10,
    RecipientAdded = 11,
    RecipientRemoved = 12,
    WorkflowStarted = 13,
    WorkflowCompleted = 14,
    WorkflowStepCompleted = 15,
    WorkflowStepRejected = 16,
    EmailSent = 17,
    ReminderSent = 18,
    DelegationAdded = 19,
    EscalationTriggered = 20,
    CommentAdded = 21,
    AttachmentUploaded = 22,
    AuthenticationEvent = 23
}
