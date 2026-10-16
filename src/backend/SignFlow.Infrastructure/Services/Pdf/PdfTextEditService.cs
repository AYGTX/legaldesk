using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Infrastructure.Services.Pdf;

/// <summary>
/// Line-level PDF text replacement: the old line is painted over and the new text drawn on top. A PDF
/// stores positioned glyphs, not paragraphs, so nothing reflows — a longer line is shrunk to fit the room
/// to the right margin instead. The covered text is NOT removed from the file's content stream.
/// </summary>
public class PdfTextEditService : IPdfTextEditService
{
    private const double Padding = 1.5;
    private const double RightMargin = 24;
    private const double MinFontSize = 6;

    public Task<Stream> ApplyEditsAsync(Stream pdf, IReadOnlyList<PdfTextEdit> edits, CancellationToken cancellationToken = default)
    {
        using var source = new MemoryStream();
        pdf.CopyTo(source);
        source.Position = 0;

        using var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify);

        foreach (var pageEdits in edits.GroupBy(e => e.Page))
        {
            if (pageEdits.Key < 1 || pageEdits.Key > document.PageCount) continue;

            var page = document.Pages[pageEdits.Key - 1];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var pageWidth = page.Width.Point;

            foreach (var edit in pageEdits)
            {
                var cover = new XRect(edit.X - Padding, edit.Y - Padding, edit.Width + 2 * Padding, edit.Height + 2 * Padding);
                gfx.DrawRectangle(XBrushes.White, cover);

                if (string.IsNullOrWhiteSpace(edit.Text)) continue;

                var fontSize = Math.Max(MinFontSize, edit.FontSize);
                var font = new XFont(AppFontResolver.FamilyName, fontSize, XFontStyleEx.Regular);

                // Never run off the page: shrink until the line fits between its start and the right margin.
                var room = pageWidth - RightMargin - edit.X;
                while (fontSize > MinFontSize && gfx.MeasureString(edit.Text, font).Width > Math.Max(room, edit.Width))
                {
                    fontSize -= 0.5;
                    font = new XFont(AppFontResolver.FamilyName, fontSize, XFontStyleEx.Regular);
                }

                gfx.DrawString(edit.Text, font, XBrushes.Black,
                    new XRect(edit.X, edit.Y, Math.Max(room, edit.Width), edit.Height), XStringFormats.CenterLeft);
            }
        }

        var output = new MemoryStream();
        document.Save(output, closeStream: false);
        output.Position = 0;
        return Task.FromResult<Stream>(output);
    }
}
