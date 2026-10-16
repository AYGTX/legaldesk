namespace SignFlow.Application.Common.Interfaces;

/// <param name="TemplateKey">
/// The signature placeholder this signature belongs to; the generator puts it where the template placed that
/// placeholder. Null for a signature with no placeholder, which goes in the signature section at the end.
/// </param>
public record HtmlTemplatePdfSignature(string ImageStoragePath, string SignerName, DateTime SignedAt, string? TemplateKey = null);

public interface IHtmlTemplatePdfGenerator
{
    /// <summary>
    /// Renders <paramref name="renderedHtmlContent"/> (with all placeholders already substituted)
    /// to a PDF stream. Signatures with a template key are placed where the template put them; the rest are
    /// appended in a signature block at the end.
    /// </summary>
    Task<Stream> GenerateAsync(
        string renderedHtmlContent,
        string title,
        List<HtmlTemplatePdfSignature> signatures,
        CancellationToken ct = default);
}
