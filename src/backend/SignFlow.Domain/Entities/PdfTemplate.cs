using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// A reusable request template backed by a pre-existing PDF that is uploaded once and persisted.
/// Every request created from this template reuses the same stored PDF as its base document — the
/// stored file is never regenerated, only copied per instance so the original stays pristine.
/// This is the PDF analogue of <see cref="HtmlTemplate"/>.
/// </summary>
public class PdfTemplate : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>Storage path of the persisted base PDF (uploaded once, reused for every instance).</summary>
    public string StoragePath { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public int PageCount { get; private set; }

    /// <summary>
    /// When set, the approver is fixed and the initiator cannot change it. When null, the initiator
    /// picks approvers at request-creation time (unless a field-level PreAssignedApprover is set).
    /// Mirrors <see cref="HtmlTemplate.DefaultApproverId"/>.
    /// </summary>
    public Guid? DefaultApproverId { get; private set; }
    public ApplicationUser? DefaultApprover { get; private set; }

    /// <summary>How recipients created from this template are routed: Sequential, Parallel, or Mixed.</summary>
    public WorkflowType WorkflowType { get; private set; } = WorkflowType.Sequential;

    public bool HideRecipientIdentities { get; private set; }

    public Guid CreatedById { get; private set; }
    public ApplicationUser CreatedByUser { get; private set; } = null!;

    public ICollection<PdfTemplateField> Fields { get; private set; } = [];

    private PdfTemplate() { }

    public static PdfTemplate Create(
        string name,
        string storagePath,
        string fileName,
        long fileSizeBytes,
        int pageCount,
        Guid createdById,
        string? description = null,
        Guid? defaultApproverId = null,
        WorkflowType workflowType = WorkflowType.Sequential,
        bool hideRecipientIdentities = false)
        => new()
        {
            Name = name,
            StoragePath = storagePath,
            FileName = fileName,
            FileSizeBytes = fileSizeBytes,
            PageCount = pageCount,
            CreatedById = createdById,
            Description = description,
            DefaultApproverId = defaultApproverId,
            WorkflowType = workflowType,
            HideRecipientIdentities = hideRecipientIdentities
        };

    /// <summary>Updates metadata/config. The stored PDF itself is immutable once uploaded.</summary>
    public void Update(
        string name,
        string? description,
        Guid? defaultApproverId,
        bool isActive,
        WorkflowType workflowType,
        bool hideRecipientIdentities)
    {
        Name = name;
        Description = description;
        DefaultApproverId = defaultApproverId;
        IsActive = isActive;
        WorkflowType = workflowType;
        HideRecipientIdentities = hideRecipientIdentities;
        UpdatedAt = DateTime.UtcNow;
    }
}
