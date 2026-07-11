namespace SignFlow.Domain.Enums;

/// <summary>
/// Field types supported by the PDF-template flow. Kept as a focused set (distinct from the broader
/// PDF-designer <see cref="FieldType"/>) so the admin config UI stays simple. Each value maps to a
/// concrete <see cref="FieldType"/> when the template field is materialized onto a document instance
/// (see PdfTemplateField.ToSignatureFieldType).
/// </summary>
public enum PdfFieldType
{
    Text,
    Date,
    Number,
    Checkbox,
    Signature,
    Dropdown
}
