using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace SignFlow.Infrastructure.Services.Pdf;

public class PdfPigService(ILogger<PdfPigService> logger) : IPdfService
{
    public Task<int> GetPageCountAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        try
        {
            using var doc = PdfDocument.Open(pdfStream, new ParsingOptions { UseLenientParsing = true });
            return Task.FromResult(doc.NumberOfPages);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get page count from PDF");
            return Task.FromResult(1);
        }
    }

    public async Task<string> ComputeHashAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(pdfStream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public Task<Stream> FlattenFieldsAsync(Stream pdfStream, IEnumerable<SignatureField> fields, CancellationToken cancellationToken = default)
    {
        // Production: use iTextSharp, PdfSharp, or Aspose.PDF for proper field flattening
        pdfStream.Position = 0;
        var ms = new MemoryStream();
        pdfStream.CopyTo(ms);
        ms.Position = 0;
        return Task.FromResult<Stream>(ms);
    }

    public Task<Stream> GenerateAuditPageAsync(Document document, IEnumerable<AuditLog> auditLogs, CancellationToken cancellationToken = default)
    {
        // Generate an HTML representation and convert to PDF in production
        var ms = new MemoryStream();
        return Task.FromResult<Stream>(ms);
    }

    /// <summary>Joins the PDFs end to end, in the order given. Each stream is read from its start.</summary>
    public async Task<Stream> MergePdfsAsync(IEnumerable<Stream> pdfs, CancellationToken cancellationToken = default)
    {
        var files = new List<byte[]>();
        foreach (var pdf in pdfs)
        {
            if (pdf.CanSeek) pdf.Position = 0;
            using var buffer = new MemoryStream();
            await pdf.CopyToAsync(buffer, cancellationToken);
            files.Add(buffer.ToArray());
        }

        if (files.Count == 0)
            throw new ArgumentException("There is nothing to merge.", nameof(pdfs));

        // One file needs no merging, and the merger does not accept a list of one.
        var merged = files.Count == 1 ? files[0] : PdfMerger.Merge(files);
        return new MemoryStream(merged);
    }

    public Task<Stream> AddMetadataAsync(Stream pdfStream, Dictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        pdfStream.Position = 0;
        var ms = new MemoryStream();
        pdfStream.CopyTo(ms);
        ms.Position = 0;
        return Task.FromResult<Stream>(ms);
    }

    public Task<Stream> AddQrCodeAsync(Stream pdfStream, string content, int page, float x, float y, float size, CancellationToken cancellationToken = default)
    {
        pdfStream.Position = 0;
        var ms = new MemoryStream();
        pdfStream.CopyTo(ms);
        ms.Position = 0;
        return Task.FromResult<Stream>(ms);
    }

    public Task<Stream> AddWatermarkAsync(Stream pdfStream, string text, CancellationToken cancellationToken = default)
    {
        pdfStream.Position = 0;
        var ms = new MemoryStream();
        pdfStream.CopyTo(ms);
        ms.Position = 0;
        return Task.FromResult<Stream>(ms);
    }

    public Task<byte[]> GenerateThumbnailAsync(Stream pdfStream, int page = 1, int width = 300, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task<Stream> EmbedSignaturesAsync(Stream pdfStream, IEnumerable<(SignatureField field, Signature signature)> signedFields, CancellationToken cancellationToken = default)
    {
        pdfStream.Position = 0;
        var ms = new MemoryStream();
        pdfStream.CopyTo(ms);
        ms.Position = 0;
        return Task.FromResult<Stream>(ms);
    }

    public Task<string> ExtractTextAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        try
        {
            pdfStream.Position = 0;
            using var doc = PdfDocument.Open(pdfStream, new ParsingOptions { UseLenientParsing = true });
            var sb = new StringBuilder();
            foreach (var page in doc.GetPages())
                sb.AppendLine(string.Join(" ", page.GetWords().Select(w => w.Text)));
            return Task.FromResult(sb.ToString());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract text from PDF");
            return Task.FromResult(string.Empty);
        }
    }
}
