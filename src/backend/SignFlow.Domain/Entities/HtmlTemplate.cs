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

    /// <summary>
    /// Request type carried by this template — requests created from it inherit this type (and its SLA)
    /// at creation time, as a snapshot. Changing this later only affects requests created afterward.
    /// </summary>
    public Guid? RequestTypeId { get; private set; }
    public RequestType? RequestType { get; private set; }


    /// <summary>
    /// The reusable approval path this template follows, when it has one. The preset then decides who approves each
    /// signature field (see <see cref="WorkflowPreset"/>); without one, the default approver and per-field choices apply.
    /// </summary>
    public Guid? WorkflowPresetId { get; private set; }
    public WorkflowPreset? WorkflowPreset { get; private set; }

    public void SetWorkflowPreset(Guid? presetId)
    {
        WorkflowPresetId = presetId;
        UpdatedAt = DateTime.UtcNow;
    }

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
        bool hideRecipientIdentities = false,
        Guid? requestTypeId = null)
        => new()
        {
            Name = name,
            HtmlContent = htmlContent,
            CreatedById = createdById,
            Description = description,
            DefaultApproverId = defaultApproverId,
            WorkflowType = workflowType,
            HideRecipientIdentities = hideRecipientIdentities,
            RequestTypeId = requestTypeId
        };

    public void Update(
        string name,
        string htmlContent,
        string? description,
        Guid? defaultApproverId,
        bool isActive,
        WorkflowType workflowType,
        bool hideRecipientIdentities,
        Guid? requestTypeId)
    {
        Name = name;
        HtmlContent = htmlContent;
        Description = description;
        DefaultApproverId = defaultApproverId;
        IsActive = isActive;
        WorkflowType = workflowType;
        HideRecipientIdentities = hideRecipientIdentities;
        RequestTypeId = requestTypeId;
        UpdatedAt = DateTime.UtcNow;
    }
}
