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
    /// <summary>SHA-256 (hex) of the signing link's token (R-M5-1). The token itself is only ever in the e-mail.</summary>
    public string? TokenHash { get; private set; }
    public DateTime? TokenExpiresAt { get; private set; }

    /// <summary>When and how the participant last proved they own the e-mail address (code by e-mail, or their account).</summary>
    public DateTime? VerifiedAt { get; private set; }
    public string? VerificationMethod { get; private set; }
    public DateTime? SignedAt { get; private set; }
    public DateTime? ViewedAt { get; private set; }
    public string? SignedIp { get; private set; }
    public string? SignedUserAgent { get; private set; }
    public string? DeclineReason { get; private set; }
    public bool ReminderSent { get; private set; }
    public DateTime? LastReminderSentAt { get; private set; }
    /// <summary>The participant this one handed their step to (R-M4-8).</summary>
    public Guid? DelegatedToId { get; private set; }
    public string? DelegationNote { get; private set; }
    /// <summary>The participant this one stands in for, by delegation.</summary>
    public Guid? DelegatedFromRecipientId { get; private set; }

    // The circuit (M4): the stage's rule, a pool assignee (team or role, R-M4-3), and the step's timing (R-M4-6).
    public StageRule StageRule { get; private set; } = StageRule.All;
    public Guid? PoolTeamId { get; private set; }
    public string? PoolRole { get; private set; }
    public int? DueInWorkingDays { get; private set; }
    public int ReminderEveryDays { get; private set; } = 3;
    public int? EscalateAfterDays { get; private set; }
    public string? EscalationEmail { get; private set; }

    /// <summary>For a pool, the member who acted for it (R-M4-3).</summary>
    public Guid? ActedByUserId { get; private set; }

    public bool IsPool => PoolTeamId is not null || PoolRole is not null;

    /// <summary>Taken out of the circuit while keeping what they did in earlier rounds (their steps, signatures and
    /// evidence): a participant removed after a round, or someone who stood in for one.</summary>
    public DateTime? RetiredAt { get; private set; }

    /// <summary>Address of a team or role step, which has no mailbox of its own: its members are written to.</summary>
    public const string PoolAddress = "pool@legaldesk.invalid";
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
            IsRequired = approvalType != ApprovalType.ViewOnly
        };

    /// <summary>How long a signing link opens the contract.</summary>
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// A new signing link: <paramref name="token"/> goes in the e-mail, only its hash is kept. Any earlier link stops
    /// working.
    /// </summary>
    public void IssueLink(string tokenHash)
    {
        TokenHash = tokenHash;
        TokenExpiresAt = DateTime.UtcNow.Add(LinkLifetime);
        UpdatedAt = DateTime.UtcNow;
    }

    public bool LinkExpired => TokenExpiresAt is { } expires && expires <= DateTime.UtcNow;

    public void MarkVerified(string method)
    {
        VerifiedAt = DateTime.UtcNow;
        VerificationMethod = method;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkViewed() => ViewedAt ??= DateTime.UtcNow;

    public void MarkSigned(string ip, string userAgent)
    {
        Status = WorkflowStepStatus.Completed;
        SignedAt = DateTime.UtcNow;
        SignedIp = ip;
        SignedUserAgent = userAgent;
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
        // Someone who stood in for a participant does not carry over to the next round (R-M4-8).
        if (DelegatedFromRecipientId is not null)
        {
            Retire();
            return;
        }
        if (RetiredAt is not null) return;
        DelegatedToId = null;
        DelegationNote = null;
        ActedByUserId = null;
        ReviewAcknowledgedAt = null;
        Status = WorkflowStepStatus.Pending;
        SignedAt = null;
        SignedIp = null;
        SignedUserAgent = null;
        DeclineReason = null;
        ReminderSent = false;
        LastReminderSentAt = null;
        // A new round sends a new link when the participant's turn comes; the old one keeps working until then.
        if (TokenHash is not null)
            TokenExpiresAt = DateTime.UtcNow.Add(LinkLifetime);
    }

    public void MarkReminderSent()
    {
        ReminderSent = true;
        LastReminderSentAt = DateTime.UtcNow;
    }

    /// <summary>Handed to <paramref name="delegateRecipientId"/> (R-M4-8): this participant's link stops working.</summary>
    public void DelegateTo(Guid delegateRecipientId, string note)
    {
        DelegatedToId = delegateRecipientId;
        DelegationNote = note;
        Status = WorkflowStepStatus.Delegated;
        TokenExpiresAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void StandsInFor(Guid recipientId) => DelegatedFromRecipientId = recipientId;

    public void SetCircuit(StageRule stageRule, Guid? poolTeamId, string? poolRole, int? dueInWorkingDays, int reminderEveryDays,
        int? escalateAfterDays, string? escalationEmail)
    {
        StageRule = stageRule;
        PoolTeamId = poolTeamId;
        PoolRole = string.IsNullOrWhiteSpace(poolRole) ? null : poolRole.Trim();
        DueInWorkingDays = dueInWorkingDays;
        ReminderEveryDays = Math.Clamp(reminderEveryDays, 0, 30);
        EscalateAfterDays = escalateAfterDays;
        EscalationEmail = string.IsNullOrWhiteSpace(escalationEmail) ? null : escalationEmail.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void ActedBy(Guid? userId) => ActedByUserId = userId;

    /// <summary>Stage closed by an "Any" rule before this participant acted (R-M4-2): their link stops working.</summary>
    public void Skip()
    {
        Status = WorkflowStepStatus.Skipped;
        if (TokenHash is not null) TokenExpiresAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Retire()
    {
        RetiredAt ??= DateTime.UtcNow;
        Skip();
    }

    /// <summary>Changes a participant of a draft circuit. Who they are only changes while they have acted in no round.</summary>
    public void Revise(string name, string email, int order, ApprovalType approvalType, string? privateMessage, string? company,
        string? roleLabel, bool identityLocked)
    {
        if (!identityLocked)
        {
            Name = name.Trim();
            Email = email.Trim();
            ApprovalType = approvalType;
            IsRequired = approvalType != ApprovalType.ViewOnly;
        }
        Order = order;
        PrivateMessage = privateMessage;
        Company = company;
        RoleLabel = roleLabel;
        UpdatedAt = DateTime.UtcNow;
    }
}
