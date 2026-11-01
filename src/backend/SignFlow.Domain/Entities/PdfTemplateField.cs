using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// An admin-configured field placed at a fixed position on a page of a <see cref="PdfTemplate"/>'s PDF.
/// Fuses the coordinate model of <see cref="SignatureField"/> (Page/X/Y/Width/Height) with the
/// configuration model of <see cref="HtmlTemplateField"/> (fill responsibility, auto-fill, pre-assigned
/// approver, workflow stage). At request-creation time each field is materialized into a real
/// <see cref="SignatureField"/> on the instance document at these coordinates.
/// </summary>
public class PdfTemplateField : TenantEntity
{
    public Guid PdfTemplateId { get; private set; }
    public PdfTemplate PdfTemplate { get; private set; } = null!;

    /// <summary>Stable key within the template — used for auto-fill mapping and as a placeholder handle.</summary>
    public string FieldKey { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;
    public PdfFieldType Type { get; private set; }

    /// <summary>Who fills this field: the requester, an approver, or the system (auto-fill).</summary>
    public PdfFieldFillRole FillRole { get; private set; }

    // ---- Placement on the page (mirrors SignatureField's coordinate model) ----
    public int Page { get; private set; }
    public float X { get; private set; }
    public float Y { get; private set; }
    public float Width { get; private set; }
    public float Height { get; private set; }

    /// <summary>
    /// Optional auto-fill source (dot-path). Profile: "user.fullName", "user.firstName", "user.lastName",
    /// "user.email", "user.company", "user.department". Request metadata: "system.today", "system.now",
    /// "request.title". Additional (e.g. external-system) sources can be added to the resolver later.
    /// When set, the field is populated automatically regardless of FillRole.
    /// </summary>
    public string? AutoFillFrom { get; private set; }

    /// <summary>Static default value stamped when no auto-fill and no filler input applies.</summary>
    public string? DefaultValue { get; private set; }

    public bool IsRequired { get; private set; }
    public int Order { get; private set; }

    /// <summary>JSON array of option strings — only relevant for the Dropdown type.</summary>
    public string? OptionsJson { get; private set; }

    /// <summary>
    /// Only relevant for Approver fields: locks this field to a specific user, overriding the template's
    /// DefaultApprover and skipping the at-request-time picker for this field. This is how "route to a
    /// specific person/role" is expressed. Null means the approver is chosen at request-creation time.
    /// </summary>
    public Guid? PreAssignedApproverId { get; private set; }
    public ApplicationUser? PreAssignedApprover { get; private set; }

    /// <summary>
    /// Only relevant for Approver fields when the template's WorkflowType is Mixed: fields sharing the
    /// same stage are worked in parallel; stages are worked through in ascending order.
    /// </summary>
    public int WorkflowStage { get; private set; } = 1;

    private PdfTemplateField() { }

    public static PdfTemplateField Create(
        Guid templateId,
        string fieldKey,
        string label,
        PdfFieldType type,
        PdfFieldFillRole fillRole,
        int page,
        float x, float y,
        float width, float height,
        bool isRequired,
        int order,
        string? autoFillFrom = null,
        string? defaultValue = null,
        string? optionsJson = null,
        Guid? preAssignedApproverId = null,
        int workflowStage = 1)
        => new()
        {
            PdfTemplateId = templateId,
            FieldKey = fieldKey,
            Label = label,
            Type = type,
            FillRole = fillRole,
            Page = page,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            IsRequired = isRequired,
            Order = order,
            AutoFillFrom = autoFillFrom,
            DefaultValue = defaultValue,
            OptionsJson = optionsJson,
            PreAssignedApproverId = preAssignedApproverId,
            WorkflowStage = workflowStage
        };

    public PdfTemplateField CopyTo(Guid templateId) =>
        Create(templateId, FieldKey, Label, Type, FillRole, Page, X, Y, Width, Height, IsRequired, Order,
            AutoFillFrom, DefaultValue, OptionsJson, PreAssignedApproverId, WorkflowStage);

    /// <summary>Maps this template field's type to the document-level <see cref="FieldType"/> used on instances.</summary>
    public FieldType ToSignatureFieldType() => Type switch
    {
        PdfFieldType.Signature => FieldType.Signature,
        PdfFieldType.Date => FieldType.Date,
        PdfFieldType.Number => FieldType.Number,
        PdfFieldType.Checkbox => FieldType.Checkbox,
        PdfFieldType.Dropdown => FieldType.Dropdown,
        PdfFieldType.Textarea => FieldType.MultilineText,
        _ => FieldType.Text
    };
}
