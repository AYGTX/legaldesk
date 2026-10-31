using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.IntegrationTests.Stubs;

internal class NullPdfService : IPdfService
{
    public Task<int> GetPageCountAsync(Stream pdfStream, CancellationToken cancellationToken = default) => Task.FromResult(1);
    public Task<string> ComputeHashAsync(Stream pdfStream, CancellationToken cancellationToken = default) => Task.FromResult("test-hash");
    public Task<Stream> FlattenFieldsAsync(Stream pdfStream, IEnumerable<SignatureField> fields, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());
    public Task<Stream> GenerateAuditPageAsync(Document document, IEnumerable<AuditLog> auditLogs, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());
    public Task<Stream> MergePdfsAsync(IEnumerable<Stream> pdfs, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());
    public Task<Stream> AddMetadataAsync(Stream pdfStream, Dictionary<string, string> metadata, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());
    public Task<Stream> AddQrCodeAsync(Stream pdfStream, string content, int page, float x, float y, float size, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());
    public Task<Stream> AddWatermarkAsync(Stream pdfStream, string text, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());
    public Task<byte[]> GenerateThumbnailAsync(Stream pdfStream, int page = 1, int width = 300, CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<byte>());
    public Task<Stream> EmbedSignaturesAsync(Stream pdfStream, IEnumerable<(SignatureField field, Signature signature)> signedFields, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());
    public virtual Task<string> ExtractTextAsync(Stream pdfStream, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

    /// <summary>Ten A4 portrait pages, unless a test says otherwise.</summary>
    public IReadOnlyList<PageSize> Pages { get; set; } = [.. Enumerable.Repeat(new PageSize(595.28, 841.89), 10)];
    public Task<IReadOnlyList<PageSize>> GetPageSizesAsync(Stream pdfStream, CancellationToken cancellationToken = default) => Task.FromResult(Pages);
}
