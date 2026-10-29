using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class Recipient : TenantEntity
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

    /// <summary>
    /// Free-text participant label shown in the UI and audit trail (e.g. "Legal team",
    /// "Compliance officer"). Capabilities come from ApprovalType, never from this label.
    /// </summary>
    public string? RoleLabel { get; private set; }

    public ICollection<SignatureField> AssignedFields { get; private set; } = [];
    public ICollection<Signature> Signatures { get; private set; } = [];
    public ICollection<ChecklistItem> ChecklistItems { get; private set; } = [];

    private Recipient() { }

    public static Recipient Create(Guid documentId, string name, string email, int order, ApprovalType approvalType, string? privateMessage = null, string? company = null, Guid? id = null, string? roleLabel = null)
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
            RoleLabel = roleLabel,
            // ViewOnly participants never gate the workflow: they are excluded from
            // turn-blocking and completion checks (both filter on IsRequired).
            IsRequired = approvalType != ApprovalType.ViewOnly,
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

    /// <summary>Non-terminal counterpart of Reject: pauses the flow until the owner revises.</summary>
    public void RequestChanges(string reason)
    {
        Status = WorkflowStepStatus.ChangesRequested;
        DeclineReason = reason;
    }

    /// <summary>
    /// Puts the recipient back into a fresh full-re-approval round after a revision upload.
    /// Signature rows and ViewedAt are kept as evidence/history; the signing link is extended
    /// so round-2 participants aren't locked out by a token minted for round 1.
    /// </summary>
    /// <summary>
    /// When this participant confirmed they had read the document and the AI review before acting. A participant who
    /// has to sign, approve or review cannot do so until this is set; a new version of the contract clears it, because
    /// what they read is no longer what they are being asked to act on.
    /// </summary>
    public DateTime? ReviewAcknowledgedAt { get; private set; }

    public void AcknowledgeReview() => ReviewAcknowledgedAt ??= DateTime.UtcNow;

    public void ResetForRevision()
    {
        ReviewAcknowledgedAt = null;
        Status = WorkflowStepStatus.Pending;
        SignedAt = null;
        SignedIp = null;
        SignedUserAgent = null;
        SignedLocation = null;
        DeclineReason = null;
        ReminderSent = false;
        LastReminderSentAt = null;
        TokenExpiresAt = DateTime.UtcNow.AddDays(30);
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
