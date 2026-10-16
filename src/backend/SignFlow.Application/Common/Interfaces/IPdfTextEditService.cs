namespace SignFlow.Application.Common.Interfaces;

/// <summary>
/// One line of text replaced on a PDF page. Coordinates are PDF points with the origin at the top-left
/// of the page (what PDF.js reports at scale 1), so a browser editor can send them unconverted.
/// </summary>
public record PdfTextEdit(int Page, double X, double Y, double Width, double Height, double FontSize, string Text);

public interface IPdfTextEditService
{
    /// <summary>
    /// Covers each edited line with a white box and draws its new text in the same place. Returns the
    /// edited PDF; the source stream is left untouched.
    /// </summary>
    Task<Stream> ApplyEditsAsync(Stream pdf, IReadOnlyList<PdfTextEdit> edits, CancellationToken cancellationToken = default);
}
