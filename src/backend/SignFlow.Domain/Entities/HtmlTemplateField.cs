using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class HtmlTemplateField : TenantEntity
{
    public Guid HtmlTemplateId { get; private set; }
    public HtmlTemplate HtmlTemplate { get; private set; } = null!;

    /// <summary>Unique key within the template, used as {{key}} placeholder in HTML content.</summary>
    public string FieldKey { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;
    public HtmlTemplateFieldType Type { get; private set; }
    public HtmlTemplateFieldRole Role { get; private set; }

    /// <summary>
    /// Optional dot-path into the initiator's profile for auto-fill.
    /// Supported values: "user.fullName", "user.firstName", "user.lastName",
    /// "user.email", "user.company".
    /// </summary>
    public string? AutoFillFrom { get; private set; }

    public bool IsRequired { get; private set; }
    public int Order { get; private set; }

    /// <summary>JSON array of option strings — only relevant for future Dropdown type.</summary>
    public string? OptionsJson { get; private set; }

    /// <summary>
    /// Only relevant for Approver Signature fields: locks this field to a specific user,
    /// overriding the template's DefaultApprover and skipping the at-request-time picker
    /// for this field. Null means the approver is chosen at request-creation time.
    /// </summary>
    public Guid? PreAssignedApproverId { get; private set; }
    public ApplicationUser? PreAssignedApprover { get; private set; }

    /// <summary>
    /// Only relevant for Approver Signature fields when the template's WorkflowType is Mixed:
    /// fields sharing the same stage sign in parallel; stages are worked through in ascending order.
    /// Ignored for Sequential/Parallel templates.
    /// </summary>
    public int WorkflowStage { get; private set; } = 1;

    /// <summary>
    /// What the field starts as on the request form when nothing else fills it (an auto-fill source wins). The
    /// initiator can change it; a blank answer takes it when the request is created.
    /// </summary>
    public string? DefaultValue { get; private set; }

    private HtmlTemplateField() { }

    public static HtmlTemplateField Create(
        Guid templateId,
        string fieldKey,
        string label,
        HtmlTemplateFieldType type,
        HtmlTemplateFieldRole role,
        bool isRequired,
        int order,
        string? autoFillFrom = null,
        string? optionsJson = null,
        Guid? preAssignedApproverId = null,
        int workflowStage = 1,
        string? defaultValue = null)
        => new()
        {
            HtmlTemplateId = templateId,
            FieldKey = fieldKey,
            Label = label,
            Type = type,
            Role = role,
            IsRequired = isRequired,
            Order = order,
            AutoFillFrom = autoFillFrom,
            OptionsJson = optionsJson,
            PreAssignedApproverId = preAssignedApproverId,
            WorkflowStage = workflowStage,
            DefaultValue = defaultValue
        };

    public HtmlTemplateField CopyTo(Guid templateId) =>
        Create(templateId, FieldKey, Label, Type, Role, IsRequired, Order, AutoFillFrom, OptionsJson, PreAssignedApproverId, WorkflowStage, DefaultValue);

    public void Update(
        string label,
        HtmlTemplateFieldType type,
        HtmlTemplateFieldRole role,
        bool isRequired,
        int order,
        string? autoFillFrom,
        string? optionsJson,
        Guid? preAssignedApproverId,
        int workflowStage,
        string? defaultValue = null)
    {
        Label = label;
        Type = type;
        Role = role;
        IsRequired = isRequired;
        Order = order;
        AutoFillFrom = autoFillFrom;
        OptionsJson = optionsJson;
        PreAssignedApproverId = preAssignedApproverId;
        WorkflowStage = workflowStage;
        DefaultValue = defaultValue;
        UpdatedAt = DateTime.UtcNow;
    }
}
