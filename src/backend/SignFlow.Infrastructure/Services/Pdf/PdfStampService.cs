using Microsoft.Extensions.Logging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Services.Pdf;

public class PdfStampService(
    IStorageService storage,
    ILogger<PdfStampService> logger) : IPdfStampService
{
    // PDF.js renders at scale=1.5 (canvas pixels per PDF point), so we divide by this
    // to convert stored canvas-pixel coordinates back to PDF points for drawing.
    private const double RenderScale = 1.5;

    public async Task<Stream> StampAsync(
        string sourcePath,
        IEnumerable<StampFieldDto> fields,
        CancellationToken cancellationToken = default)
    {
        var pdfStream = await storage.DownloadAsync(sourcePath, cancellationToken);
        var pdfBytes = await ReadAllBytesAsync(pdfStream, cancellationToken);

        using var ms = new MemoryStream(pdfBytes);
        PdfDocument doc;
        try
        {
            doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PDFsharp could not open PDF for stamping — returning original");
            return new MemoryStream(pdfBytes);
        }

        foreach (var field in fields)
        {
            if (field.Page < 1 || field.Page > doc.PageCount) continue;
            var page = doc.Pages[field.Page - 1];

            using var gfx = XGraphics.FromPdfPage(page);

            double x = field.X / RenderScale;
            double y = field.Y / RenderScale;
            double w = field.Width / RenderScale;
            double h = field.Height / RenderScale;

            if (!string.IsNullOrEmpty(field.ImageStoragePath))
            {
                await DrawImageFieldAsync(gfx, field.ImageStoragePath, x, y, w, h, cancellationToken);
            }
            else if (field.FieldType == FieldType.Checkbox)
            {
                DrawCheckbox(gfx, field.TextValue == "true", x, y, w, h);
            }
            else if (!string.IsNullOrEmpty(field.TextValue))
            {
                DrawText(gfx, field.TextValue, x, y, w, h);
            }
        }

        var output = new MemoryStream();
        doc.Save(output);
        doc.Dispose();
        output.Position = 0;
        return output;
    }

    private async Task DrawImageFieldAsync(XGraphics gfx, string imagePath, double x, double y, double w, double h, CancellationToken ct)
    {
        try
        {
            var imgStream = await storage.DownloadAsync(imagePath, ct);
            var imgBytes = await ReadAllBytesAsync(imgStream, ct);
            using var imgMs = new MemoryStream(imgBytes);
            var xImage = XImage.FromStream(imgMs);
            // Add small padding so the signature doesn't clip to the field border
            gfx.DrawImage(xImage, x + 2, y + 2, w - 4, h - 4);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not draw signature image from {Path}", imagePath);
        }
    }

    private static void DrawCheckbox(XGraphics gfx, bool isChecked, double x, double y, double w, double h)
    {
        var pen = new XPen(XColors.Black, 1);
        gfx.DrawRectangle(pen, x + 1, y + 1, w - 2, h - 2);
        if (isChecked)
        {
            var checkPen = new XPen(XColors.Black, 2);
            // Draw checkmark inside the box
            gfx.DrawLine(checkPen, x + w * 0.15, y + h * 0.55, x + w * 0.42, y + h * 0.8);
            gfx.DrawLine(checkPen, x + w * 0.42, y + h * 0.8, x + w * 0.85, y + h * 0.2);
        }
    }

    private static void DrawText(XGraphics gfx, string text, double x, double y, double w, double h)
    {
        var fontSize = Math.Max(6, Math.Min(h * 0.65, 11));
        var font = new XFont("Arial", fontSize, XFontStyleEx.Regular);
        var rect = new XRect(x + 2, y, w - 4, h);
        // Clip to field area so long text doesn't overflow onto the PDF
        gfx.Save();
        gfx.IntersectClip(rect);
        gfx.DrawString(text, font, XBrushes.Black, rect, XStringFormats.CenterLeft);
        gfx.Restore();
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        using var buf = new MemoryStream();
        await stream.CopyToAsync(buf, ct);
        return buf.ToArray();
    }
}
