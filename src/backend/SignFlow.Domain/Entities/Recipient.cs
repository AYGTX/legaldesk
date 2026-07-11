using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class Recipient : BaseEntity
{
    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public ApprovalType ApprovalType { get; private set; }
    public WorkflowStepStatus Status { get; private set; } = WorkflowStepStatus.Pending;
    public string SecureToken { get; private set; } = Guid.NewGuid().ToString("N");
    public DateTime? TokenExpiresAt { get; private set; }
    public DateTime? SignedAt { get; private set; }
    public DateTime? ViewedAt { get; private set; }
    public string? SignedIp { get; private set; }
    public string? SignedUserAgent { get; private set; }
    public string? SignedLocation { get; private set; }
    public string? DeclineReason { get; private set; }
    public bool ReminderSent { get; private set; }
    public DateTime? LastReminderSentAt { get; private set; }
    public Guid? DelegatedToId { get; private set; }
    public string? DelegationNote { get; private set; }
    public bool IsRequired { get; private set; } = true;
    public string? PrivateMessage { get; private set; }
    public string? Company { get; private set; }

    public ICollection<SignatureField> AssignedFields { get; private set; } = [];
    public ICollection<Signature> Signatures { get; private set; } = [];

    private Recipient() { }

    public static Recipient Create(Guid documentId, string name, string email, int order, ApprovalType approvalType, string? privateMessage = null, string? company = null, Guid? id = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            DocumentId = documentId,
            Name = name,
            Email = email,
            Order = order,
            ApprovalType = approvalType,
            PrivateMessage = privateMessage,
            Company = company,
            TokenExpiresAt = DateTime.UtcNow.AddDays(30)
        };

    public void MarkViewed() => ViewedAt ??= DateTime.UtcNow;

    public void MarkSigned(string ip, string userAgent, string? location = null)
    {
        Status = WorkflowStepStatus.Completed;
        SignedAt = DateTime.UtcNow;
        SignedIp = ip;
        SignedUserAgent = userAgent;
        SignedLocation = location;
    }

    public void Reject(string reason)
    {
        Status = WorkflowStepStatus.Rejected;
        DeclineReason = reason;
    }

    public void MarkReminderSent()
    {
        ReminderSent = true;
        LastReminderSentAt = DateTime.UtcNow;
    }

    public void Delegate(Guid userId, string note)
    {
        DelegatedToId = userId;
        DelegationNote = note;
        Status = WorkflowStepStatus.Delegated;
    }
}
