using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Infrastructure.Services.Pdf;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// Runs the real PDF edit service on a real PDF and reads the result back with the same text extractor the
/// AI review uses — the edit must land in the file, not just in the browser.
/// </summary>
public class PdfTextEditTests
{
    private const string Line = "Invoices are payable within 30 days of the invoice date.";

    public PdfTextEditTests()
    {
        PdfSharp.Fonts.GlobalFontSettings.FontResolver ??= new AppFontResolver();
    }

    private static MemoryStream BuildPdf()
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont(AppFontResolver.FamilyName, 11, XFontStyleEx.Regular);
            gfx.DrawString("Article 2 - Payment", font, XBrushes.Black, new XPoint(56, 80));
            gfx.DrawString(Line, font, XBrushes.Black, new XPoint(56, 100));
        }
        var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Edited_line_appears_in_the_extracted_text_and_the_page_count_is_unchanged()
    {
        var service = new PdfTextEditService();
        var pdfService = new PdfPigService(NullLogger<PdfPigService>.Instance);

        await using var source = BuildPdf();
        await using var edited = await service.ApplyEditsAsync(source,
        [
            new PdfTextEdit(Page: 1, X: 56, Y: 89, Width: 330, Height: 14, FontSize: 11,
                Text: "Invoices are payable within 45 days of the invoice date.")
        ]);

        Assert.True(edited.Length > 0);

        edited.Position = 0;
        Assert.Equal(1, await pdfService.GetPageCountAsync(edited));

        edited.Position = 0;
        var text = await pdfService.ExtractTextAsync(edited);
        Assert.Contains("within 45 days", text);
        Assert.Contains("Article 2 - Payment", text);   // untouched lines survive
    }

    [Fact]
    public async Task The_source_stream_is_not_modified()
    {
        var service = new PdfTextEditService();
        await using var source = BuildPdf();
        var before = source.Length;

        await using var edited = await service.ApplyEditsAsync(source,
            [new PdfTextEdit(1, 56, 89, 330, 14, 11, "Changed")]);

        Assert.Equal(before, source.Length);
        Assert.NotEqual(before, edited.Length);
    }

    [Fact]
    public async Task An_edit_for_a_page_that_does_not_exist_is_ignored()
    {
        var service = new PdfTextEditService();
        await using var source = BuildPdf();

        await using var edited = await service.ApplyEditsAsync(source,
            [new PdfTextEdit(7, 56, 89, 330, 14, 11, "Nowhere")]);

        var pdfService = new PdfPigService(NullLogger<PdfPigService>.Instance);
        edited.Position = 0;
        Assert.DoesNotContain("Nowhere", await pdfService.ExtractTextAsync(edited));
    }
}
