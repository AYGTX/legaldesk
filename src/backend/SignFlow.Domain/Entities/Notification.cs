using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class Notification : TenantEntity
{
    public Guid? UserId { get; private set; }
    public ApplicationUser? User { get; private set; }
    public string? RecipientEmail { get; private set; }
    public NotificationType Type { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public bool IsRead { get; private set; }
    public DateTime? ReadAt { get; private set; }
    public bool IsSent { get; private set; }
    public DateTime? SentAt { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int RetryCount { get; private set; }
    public Guid? DocumentId { get; private set; }
    public string? Metadata { get; private set; }
    public bool IsEmail { get; private set; }
    public bool IsPush { get; private set; }

    private Notification() { }

    public static Notification Create(
        NotificationType type,
        string subject,
        string body,
        Guid? userId = null,
        string? recipientEmail = null,
        Guid? documentId = null,
        bool isEmail = true,
        bool isPush = false)
        => new()
        {
            Type = type,
            Subject = subject,
            Body = body,
            UserId = userId,
            RecipientEmail = recipientEmail,
            DocumentId = documentId,
            IsEmail = isEmail,
            IsPush = isPush
        };

    public void MarkSent() { IsSent = true; SentAt = DateTime.UtcNow; }
    public void MarkRead() { IsRead = true; ReadAt = DateTime.UtcNow; }
    public void SetError(string error) { ErrorMessage = error; RetryCount++; }
}
