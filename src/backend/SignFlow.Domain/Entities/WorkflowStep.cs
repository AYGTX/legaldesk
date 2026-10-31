using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class WorkflowStep : TenantEntity
{
    public Guid WorkflowId { get; private set; }
    /// <summary>The round this step belongs to (R-M7-2): earlier rounds keep their steps.</summary>
    public Guid? RoundId { get; private set; }
    /// <summary>The stage's rule (R-M4-2), the same on every step of the stage.</summary>
    public StageRule StageRule { get; private set; } = StageRule.All;
    public Workflow Workflow { get; private set; } = null!;
    public int Order { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ApprovalType ApprovalType { get; private set; }
    public WorkflowStepStatus Status { get; private set; } = WorkflowStepStatus.Pending;
    public Guid RecipientId { get; private set; }
    public Recipient Recipient { get; private set; } = null!;
    public bool IsParallel { get; private set; }
    public int? ParallelGroupId { get; private set; }
    public DateTime? DueAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public bool SendReminders { get; private set; } = true;
    public int ReminderDays { get; private set; } = 3;
    public string? EscalationEmail { get; private set; }
    public int? EscalationDays { get; private set; }
    public string? Instructions { get; private set; }
    public bool Required { get; private set; } = true;
    public string? DeclineReason { get; private set; }

    // Follow-up (R-M4-6): when the step opened, the next reminder, how many were sent, and the escalation.
    public DateTime? OpenedAt { get; private set; }
    public DateTime? NextReminderAt { get; private set; }
    public int ReminderCount { get; private set; }
    public DateTime? EscalatedAt { get; private set; }

    /// <summary>For a pool step (team or role), the member who acted for it (R-M4-3).</summary>
    public Guid? ActedByUserId { get; private set; }

    /// <summary>The step this one took over by delegation (R-M4-8).</summary>
    public Guid? DelegatedFromStepId { get; private set; }

    public const int MaxReminders = 5;

    private WorkflowStep() { }

    public static WorkflowStep Create(Guid workflowId, int order, string name, ApprovalType approvalType, Guid recipientId, bool isParallel = false,
        Guid? roundId = null, StageRule stageRule = StageRule.All, Guid? delegatedFromStepId = null)
        => new()
        {
            WorkflowId = workflowId,
            Order = order,
            Name = name,
            ApprovalType = approvalType,
            RecipientId = recipientId,
            IsParallel = isParallel,
            RoundId = roundId,
            StageRule = stageRule,
            DelegatedFromStepId = delegatedFromStepId
        };

    public void Start() { Status = WorkflowStepStatus.InProgress; }

    /// <summary>The step's stage opened: it is due at <paramref name="dueAt"/> and reminded every
    /// <paramref name="reminderDays"/> days until it is done (R-M4-6).</summary>
    public void Open(DateTime now, DateTime? dueAt, int reminderDays, int? escalationDays, string? escalationEmail)
    {
        Status = WorkflowStepStatus.InProgress;
        OpenedAt = now;
        DueAt = dueAt;
        ReminderDays = Math.Max(1, reminderDays);
        SendReminders = reminderDays > 0;
        NextReminderAt = SendReminders ? now.AddDays(ReminderDays) : null;
        EscalationDays = escalationDays;
        EscalationEmail = escalationEmail;
    }

    public void ReminderSent(DateTime now)
    {
        ReminderCount++;
        NextReminderAt = ReminderCount >= MaxReminders ? null : now.AddDays(ReminderDays);
    }

    public void Escalated(DateTime now) => EscalatedAt = now;

    public void ActedBy(Guid? userId) => ActedByUserId = userId;

    /// <summary>Handed to someone else (R-M4-8): this step is closed; the delegate's step continues.</summary>
    public void Delegated()
    {
        Status = WorkflowStepStatus.Delegated;
        CompletedAt = DateTime.UtcNow;
        NextReminderAt = null;
    }

    public void Complete()
    {
        Status = WorkflowStepStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        NextReminderAt = null;
    }

    public void Reject(string reason)
    {
        Status = WorkflowStepStatus.Rejected;
        DeclineReason = reason;
        CompletedAt = DateTime.UtcNow;
    }

    public void Skip() { Status = WorkflowStepStatus.Skipped; CompletedAt = DateTime.UtcNow; NextReminderAt = null; }

    /// <summary>Non-terminal counterpart of Reject — the round pauses, so no CompletedAt.</summary>
    public void RequestChanges(string reason)
    {
        Status = WorkflowStepStatus.ChangesRequested;
        DeclineReason = reason;
    }

    public void SetDueDate(DateTime dueAt) => DueAt = dueAt;
    public void SetEscalation(string email, int days) { EscalationEmail = email; EscalationDays = days; }
    public void SetParallelGroup(int groupId) { IsParallel = true; ParallelGroupId = groupId; }
}
