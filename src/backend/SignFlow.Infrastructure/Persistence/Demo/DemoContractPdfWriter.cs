using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace SignFlow.Infrastructure.Persistence.Demo;

/// <summary>
/// Renders a seeded demo contract into a real, text-bearing A4 PDF.
///
/// The demo portfolio deliberately ships actual files rather than database rows pointing at
/// nothing: the preview, the download, the hash and — most importantly — re-running the AI review
/// all read the stored file, so a contract whose PDF does not exist looks broken the moment anyone
/// clicks it. Because the text is drawn as text (not an image), <c>PdfPigService.ExtractTextAsync</c>
/// reads it back and a live re-analysis works on the same wording the reviewer sees on screen.
///
/// Layout is deliberately minimal — PDFsharp draws strings, it does not lay out documents, so this
/// wraps and paginates by hand. It is enough for a legible contract and nothing more.
/// </summary>
internal static class DemoContractPdfWriter
{
    private const double PageMargin = 56;      // ~2 cm
    private const double LineHeight = 14;
    private const double HeadingSpaceBefore = 12;
    private const double ParagraphSpaceAfter = 8;

    /// <summary>One block of the contract body: a numbered article heading or a paragraph under it.</summary>
    internal record ContractBlock(string Text, bool IsHeading);

    public static MemoryStream Render(string title, string reference, IReadOnlyList<ContractBlock> blocks)
    {
        using var document = new PdfDocument();
        document.Info.Title = title;

        var titleFont = new XFont(FontFamily, 15, XFontStyleEx.Bold);
        var referenceFont = new XFont(FontFamily, 9, XFontStyleEx.Italic);
        var headingFont = new XFont(FontFamily, 11, XFontStyleEx.Bold);
        var bodyFont = new XFont(FontFamily, 10, XFontStyleEx.Regular);

        var page = NewPage(document);
        var gfx = XGraphics.FromPdfPage(page);
        var contentWidth = page.Width.Point - 2 * PageMargin;
        var y = PageMargin;

        gfx.DrawString(title, titleFont, XBrushes.Black, new XPoint(PageMargin, y));
        y += LineHeight + 4;
        gfx.DrawString(reference, referenceFont, XBrushes.Gray, new XPoint(PageMargin, y));
        y += LineHeight * 1.5;

        foreach (var block in blocks)
        {
            var font = block.IsHeading ? headingFont : bodyFont;
            if (block.IsHeading) y += HeadingSpaceBefore;

            foreach (var line in WrapLines(gfx, block.Text, font, contentWidth))
            {
                if (y + LineHeight > page.Height.Point - PageMargin)
                {
                    gfx.Dispose();
                    page = NewPage(document);
                    gfx = XGraphics.FromPdfPage(page);
                    y = PageMargin;
                }

                gfx.DrawString(line, font, XBrushes.Black, new XPoint(PageMargin, y));
                y += LineHeight;
            }

            y += ParagraphSpaceAfter;
        }

        gfx.Dispose();

        var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        stream.Position = 0;
        return stream;
    }

    /// <summary>The single family <see cref="Services.Pdf.AppFontResolver"/> knows how to resolve.</summary>
    private const string FontFamily = Services.Pdf.AppFontResolver.FamilyName;

    private static PdfPage NewPage(PdfDocument document)
    {
        var page = document.AddPage();
        page.Size = PageSize.A4;
        return page;
    }

    /// <summary>Greedy word wrap. Words longer than the line are left to overflow rather than hyphenated.</summary>
    private static List<string> WrapLines(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (current.Length > 0 && gfx.MeasureString(candidate, font).Width > maxWidth)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0) lines.Add(current);
        return lines;
    }
}
