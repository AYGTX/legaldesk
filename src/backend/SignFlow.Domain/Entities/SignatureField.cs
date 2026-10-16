using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class SignatureField : BaseEntity
{
    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public Guid? RecipientId { get; private set; }
    public Recipient? Recipient { get; private set; }
    public FieldType FieldType { get; private set; }
    public int Page { get; private set; }
    public float X { get; private set; }
    public float Y { get; private set; }
    public float Width { get; private set; }
    public float Height { get; private set; }
    public float? Rotation { get; private set; }
    public string? Label { get; private set; }
    public string? Placeholder { get; private set; }
    public string? DefaultValue { get; private set; }
    public string? FontFamily { get; private set; }
    public float? FontSize { get; private set; }
    public string? FontColor { get; private set; }
    public string? BorderColor { get; private set; }
    public string? BackgroundColor { get; private set; }
    public bool IsRequired { get; private set; } = true;
    public bool IsReadOnly { get; private set; }
    public string? ValidationRule { get; private set; }
    public string? Options { get; private set; }
    public int ZIndex { get; private set; }
    public string? VisibilityRules { get; private set; }
    public string? GroupName { get; private set; }
    public bool IsSigned { get; private set; }
    public DateTime? SignedAt { get; private set; }
    public string? SignedValue { get; private set; }
    public string? FieldName { get; private set; }
    public string? Tooltip { get; private set; }
    public int? MaxLength { get; private set; }
    public int? MinLength { get; private set; }

    /// <summary>
    /// One row per signing act. Usually a single row, but a field re-signed after a
    /// revision (request-changes loop) accumulates one row per round — earlier rows are
    /// kept as evidence and never deleted.
    /// </summary>
    public ICollection<Signature> Signatures { get; private set; } = [];

    private SignatureField() { }

    public static SignatureField Create(
        Guid documentId,
        FieldType fieldType,
        int page,
        float x, float y,
        float width, float height,
        Guid? recipientId = null)
        => new()
        {
            DocumentId = documentId,
            FieldType = fieldType,
            Page = page,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            RecipientId = recipientId
        };

    public void UpdatePosition(int page, float x, float y, float width, float height, float? rotation = null)
    {
        Page = page;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Rotation = rotation;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateStyle(string? fontFamily, float? fontSize, string? fontColor, string? borderColor, string? backgroundColor)
    {
        FontFamily = fontFamily;
        FontSize = fontSize;
        FontColor = fontColor;
        BorderColor = borderColor;
        BackgroundColor = backgroundColor;
    }

    public void UpdateProperties(string? label, string? placeholder, string? defaultValue, bool isRequired, bool isReadOnly, string? validationRule, string? tooltip, int? maxLength)
    {
        Label = label;
        Placeholder = placeholder;
        DefaultValue = defaultValue;
        IsRequired = isRequired;
        IsReadOnly = isReadOnly;
        ValidationRule = validationRule;
        Tooltip = tooltip;
        MaxLength = maxLength;
    }

    public void SetSigned(string value)
    {
        IsSigned = true;
        SignedAt = DateTime.UtcNow;
        SignedValue = value;
    }

    /// <summary>
    /// Voids the field's completion after a revision upload so it must be re-signed.
    /// The Signature rows themselves are kept — voiding erases completion state, not evidence.
    /// </summary>
    public void VoidSignature()
    {
        IsSigned = false;
        SignedAt = null;
        SignedValue = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AssignToRecipient(Guid recipientId) => RecipientId = recipientId;

    public void SetZIndex(int zIndex) => ZIndex = zIndex;
    /// <summary>
    /// For a contract built from an HTML template: the key of the signature placeholder this field stands for
    /// (<c>{{legalSignature}}</c>), so the signature can be shown where the template put it. Null for every other field.
    /// </summary>
    public string? TemplateKey { get; private set; }

    public void SetTemplateKey(string? key) => TemplateKey = key;

    public void SetOptions(string options) => Options = options;
    public void SetGroupName(string groupName) => GroupName = groupName;
    public void SetVisibilityRules(string rules) => VisibilityRules = rules;
}
