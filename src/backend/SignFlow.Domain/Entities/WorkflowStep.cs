using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class WorkflowStep : TenantEntity
{
    public Guid WorkflowId { get; private set; }
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
    public string? ConditionalExpression { get; private set; }
    public bool SendReminders { get; private set; } = true;
    public int ReminderDays { get; private set; } = 3;
    public string? EscalationEmail { get; private set; }
    public int? EscalationDays { get; private set; }
    public string? Instructions { get; private set; }
    public bool Required { get; private set; } = true;
    public string? DeclineReason { get; private set; }

    private WorkflowStep() { }

    public static WorkflowStep Create(Guid workflowId, int order, string name, ApprovalType approvalType, Guid recipientId, bool isParallel = false)
        => new()
        {
            WorkflowId = workflowId,
            Order = order,
            Name = name,
            ApprovalType = approvalType,
            RecipientId = recipientId,
            IsParallel = isParallel
        };

    public void Start() { Status = WorkflowStepStatus.InProgress; }

    public void Complete()
    {
        Status = WorkflowStepStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void Reject(string reason)
    {
        Status = WorkflowStepStatus.Rejected;
        DeclineReason = reason;
        CompletedAt = DateTime.UtcNow;
    }

    public void Skip() { Status = WorkflowStepStatus.Skipped; CompletedAt = DateTime.UtcNow; }

    /// <summary>Non-terminal counterpart of Reject — the round pauses, so no CompletedAt.</summary>
    public void RequestChanges(string reason)
    {
        Status = WorkflowStepStatus.ChangesRequested;
        DeclineReason = reason;
    }

    public void SetDueDate(DateTime dueAt) => DueAt = dueAt;
    public void SetConditionalExpression(string expression) => ConditionalExpression = expression;
    public void SetEscalation(string email, int days) { EscalationEmail = email; EscalationDays = days; }
    public void SetParallelGroup(int groupId) { IsParallel = true; ParallelGroupId = groupId; }
}
