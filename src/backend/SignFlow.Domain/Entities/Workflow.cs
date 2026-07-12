using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class Workflow : BaseEntity
{
    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public WorkflowType Type { get; private set; }
    public WorkflowStepStatus Status { get; private set; } = WorkflowStepStatus.Pending;
    public int CurrentStepOrder { get; private set; } = 1;
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public bool AllowParallelSigning { get; private set; }
    public bool RequireAllToSign { get; private set; } = true;
    public string? CompletionMessage { get; private set; }

    /// <summary>SQL Server rowversion concurrency token — two simultaneous stage advances conflict instead of double-advancing.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public ICollection<WorkflowStep> Steps { get; private set; } = [];

    private Workflow() { }

    public static Workflow Create(Guid documentId, WorkflowType type, bool allowParallel = false)
        => new()
        {
            DocumentId = documentId,
            Type = type,
            AllowParallelSigning = allowParallel
        };

    public void Start()
    {
        Status = WorkflowStepStatus.InProgress;
        StartedAt = DateTime.UtcNow;
    }

    public void Complete()
    {
        Status = WorkflowStepStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void Reject()
    {
        Status = WorkflowStepStatus.Rejected;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>Pauses the workflow while the owner revises — non-terminal, so no CompletedAt.</summary>
    public void RequestChanges() => Status = WorkflowStepStatus.ChangesRequested;

    public void AdvanceToNextStep() => CurrentStepOrder++;
}
