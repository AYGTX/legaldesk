using Microsoft.Extensions.Logging;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Services.Pdf;

public class PdfStampService(
    IStorageService storage,
    ILogger<PdfStampService> logger) : IPdfStampService
{
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

            // Fields are in points from the top-left of the page as displayed (R-M3-1). Draw around the box's centre,
            // turned so the content reads upright on the displayed page, plus the field's own rotation (R-M3-2).
            var placement = PageGeometry.Place(PageGeometry.Rotation(page.Rotate), page.MediaBox.Width, page.MediaBox.Height,
                field.X, field.Y, field.Width, field.Height, field.Rotation);
            gfx.Save();
            gfx.TranslateTransform(placement.CenterX, placement.CenterY);
            gfx.RotateTransform(placement.Angle);
            double w = field.Width;
            double h = field.Height;
            double x = -w / 2;
            double y = -h / 2;

            if (!string.IsNullOrEmpty(field.ImageStoragePath))
            {
                await DrawImageFieldAsync(gfx, field.ImageStoragePath, x, y, w, h, cancellationToken);
            }
            else if (field.FieldType == FieldType.Checkbox)
            {
                DrawCheckbox(gfx, field.TextValue == "true", x, y, w, h);
            }
            else if (field.FieldType == FieldType.Radio)
            {
                // Only signed fields reach stamping, so a radio here is always the selected option.
                DrawRadio(gfx, x, y, w, h);
            }
            else if (!string.IsNullOrEmpty(field.TextValue))
            {
                if (field.FieldType == FieldType.MultilineText) DrawWrappedText(gfx, field.TextValue, x, y, w, h);
                else DrawText(gfx, field.TextValue, x, y, w, h);
            }
            gfx.Restore();
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

    private static void DrawRadio(XGraphics gfx, double x, double y, double w, double h)
    {
        // Outer ring + filled inner dot, centered in the field box.
        var d = Math.Min(w, h) - 2;
        var cx = x + w / 2;
        var cy = y + h / 2;
        var pen = new XPen(XColors.Black, 1);
        gfx.DrawEllipse(pen, cx - d / 2, cy - d / 2, d, d);
        var dot = d * 0.5;
        gfx.DrawEllipse(XBrushes.Black, cx - dot / 2, cy - dot / 2, dot, dot);
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

    /// <summary>
    /// A multi-line field: the text wraps inside the box instead of running off its right edge, and anything that
    /// still does not fit is clipped at the bottom rather than drawn over the page.
    /// </summary>
    private static void DrawWrappedText(XGraphics gfx, string text, double x, double y, double w, double h)
    {
        var font = new XFont("Arial", 10, XFontStyleEx.Regular);
        var rect = new XRect(x + 2, y + 1, Math.Max(10, w - 4), Math.Max(10, h - 2));
        gfx.Save();
        gfx.IntersectClip(rect);
        new XTextFormatter(gfx) { Alignment = XParagraphAlignment.Left }.DrawString(text, font, XBrushes.Black, rect, XStringFormats.TopLeft);
        gfx.Restore();
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        using var buf = new MemoryStream();
        await stream.CopyToAsync(buf, ct);
        return buf.ToArray();
    }
}

/// <summary>
/// From the displayed page (what PDF.js shows, after the page's /Rotate) to the page's own drawing space. /Rotate turns
/// the page clockwise for display, so a point (x, y) measured from the displayed top-left is, on the unrotated page:
/// 90° → (y, H − x); 180° → (W − x, H − y); 270° → (W − y, x), with W × H the unrotated page.
/// </summary>
public static class PageGeometry
{
    public readonly record struct Placement(double CenterX, double CenterY, double Angle);

    public static int Rotation(int rotate) => ((rotate % 360) + 360) % 360 / 90 * 90;

    public static (double X, double Y) ToPage(int rotation, double pageWidth, double pageHeight, double x, double y) => rotation switch
    {
        90 => (y, pageHeight - x),
        180 => (pageWidth - x, pageHeight - y),
        270 => (pageWidth - y, x),
        _ => (x, y)
    };

    /// <param name="fieldRotation">Degrees clockwise on the displayed page (0, 90, 180, 270).</param>
    public static Placement Place(int rotation, double pageWidth, double pageHeight, double x, double y, double width, double height,
        double? fieldRotation)
    {
        var (cx, cy) = ToPage(rotation, pageWidth, pageHeight, x + width / 2, y + height / 2);
        // Undo the display rotation so the content reads upright, then apply the field's own.
        return new Placement(cx, cy, (fieldRotation ?? 0) - rotation);
    }
}
