namespace SignFlow.Domain.Enums;

public enum HtmlTemplateFieldType
{
    Text,
    Textarea,
    Date,
    Number,
    Checkbox,
    Signature,

    /// <summary>One of a fixed list of choices (<c>OptionsJson</c>), like a PDF template's dropdown.</summary>
    Dropdown
}
