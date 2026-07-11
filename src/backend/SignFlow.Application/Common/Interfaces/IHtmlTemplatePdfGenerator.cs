namespace SignFlow.Application.Common.Interfaces;

public record HtmlTemplatePdfSignature(string ImageStoragePath, string SignerName, DateTime SignedAt);

public interface IHtmlTemplatePdfGenerator
{
    /// <summary>
    /// Renders <paramref name="renderedHtmlContent"/> (with all placeholders already substituted)
    /// to a PDF stream, appending a signature block at the end.
    /// </summary>
    Task<Stream> GenerateAsync(
        string renderedHtmlContent,
        string title,
        List<HtmlTemplatePdfSignature> signatures,
        CancellationToken ct = default);
}
