namespace SignFlow.Application.Common.Interfaces;

public interface IAiService
{
    Task<string> SummarizeDocumentAsync(string text, string title, CancellationToken ct = default);
    Task<GeneratedHtmlTemplate> GenerateHtmlFromTextAsync(string userText, CancellationToken ct = default);
}

public record GeneratedHtmlTemplateField(
    string FieldKey,
    string Label,
    string Type,   // Text | Textarea | Date | Number | Checkbox | Signature
    string Role,   // Initiator | Approver
    bool IsRequired,
    int Order,
    string? AutoFillFrom);

public record GeneratedHtmlTemplate(
    string HtmlContent,
    string SuggestedName,
    List<GeneratedHtmlTemplateField> Fields);
