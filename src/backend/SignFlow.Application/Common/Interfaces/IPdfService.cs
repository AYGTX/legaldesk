using SignFlow.Domain.Entities;

namespace SignFlow.Application.Common.Interfaces;

public interface IPdfService
{
    Task<int> GetPageCountAsync(Stream pdfStream, CancellationToken cancellationToken = default);
    Task<string> ComputeHashAsync(Stream pdfStream, CancellationToken cancellationToken = default);
    Task<Stream> FlattenFieldsAsync(Stream pdfStream, IEnumerable<SignatureField> fields, CancellationToken cancellationToken = default);
    Task<Stream> GenerateAuditPageAsync(Document document, IEnumerable<AuditLog> auditLogs, CancellationToken cancellationToken = default);
    Task<Stream> MergePdfsAsync(IEnumerable<Stream> pdfs, CancellationToken cancellationToken = default);
    Task<Stream> AddMetadataAsync(Stream pdfStream, Dictionary<string, string> metadata, CancellationToken cancellationToken = default);
    Task<Stream> AddQrCodeAsync(Stream pdfStream, string content, int page, float x, float y, float size, CancellationToken cancellationToken = default);
    Task<Stream> AddWatermarkAsync(Stream pdfStream, string text, CancellationToken cancellationToken = default);
    Task<byte[]> GenerateThumbnailAsync(Stream pdfStream, int page = 1, int width = 300, CancellationToken cancellationToken = default);
    Task<Stream> EmbedSignaturesAsync(Stream pdfStream, IEnumerable<(SignatureField field, Signature signature)> signedFields, CancellationToken cancellationToken = default);
    Task<string> ExtractTextAsync(Stream pdfStream, CancellationToken cancellationToken = default);

    /// <summary>Each page's size in points as displayed (after its own /Rotate), in page order (R-M3-1).</summary>
    Task<IReadOnlyList<PageSize>> GetPageSizesAsync(Stream pdfStream, CancellationToken cancellationToken = default);
}

public readonly record struct PageSize(double Width, double Height);
