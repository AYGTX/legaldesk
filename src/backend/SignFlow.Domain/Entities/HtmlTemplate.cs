using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class HtmlTemplate : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>
    /// Raw HTML content with {{fieldKey}} placeholders where fields are rendered.
    /// </summary>
    public string HtmlContent { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// When set, the approver is fixed and the initiator cannot change it.
    /// When null, the initiator picks an approver at request-creation time (unless a
    /// field-level PreAssignedApprover is set on the relevant Approver Signature field).
    /// </summary>
    public Guid? DefaultApproverId { get; private set; }
    public ApplicationUser? DefaultApprover { get; private set; }

    /// <summary>How recipients created from this template are routed: Sequential, Parallel, or Mixed.</summary>
    public WorkflowType WorkflowType { get; private set; } = WorkflowType.Sequential;

    public bool HideRecipientIdentities { get; private set; }

    public Guid CreatedById { get; private set; }
    public ApplicationUser CreatedByUser { get; private set; } = null!;

    public ICollection<HtmlTemplateField> Fields { get; private set; } = [];

    private HtmlTemplate() { }

    public static HtmlTemplate Create(
        string name,
        string htmlContent,
        Guid createdById,
        string? description = null,
        Guid? defaultApproverId = null,
        WorkflowType workflowType = WorkflowType.Sequential,
        bool hideRecipientIdentities = false)
        => new()
        {
            Name = name,
            HtmlContent = htmlContent,
            CreatedById = createdById,
            Description = description,
            DefaultApproverId = defaultApproverId,
            WorkflowType = workflowType,
            HideRecipientIdentities = hideRecipientIdentities
        };

    public void Update(
        string name,
        string htmlContent,
        string? description,
        Guid? defaultApproverId,
        bool isActive,
        WorkflowType workflowType,
        bool hideRecipientIdentities)
    {
        Name = name;
        HtmlContent = htmlContent;
        Description = description;
        DefaultApproverId = defaultApproverId;
        IsActive = isActive;
        WorkflowType = workflowType;
        HideRecipientIdentities = hideRecipientIdentities;
        UpdatedAt = DateTime.UtcNow;
    }
}
