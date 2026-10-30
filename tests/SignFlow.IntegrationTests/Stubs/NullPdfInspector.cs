using SignFlow.Application.Common.Services;

namespace SignFlow.IntegrationTests.Stubs;

/// <summary>Accepts any file as a readable one-page PDF; scenario tests exercise workflows, not upload checks
/// (those are PdfUploadGuardTests, against real PDFs).</summary>
internal sealed class NullPdfInspector : IPdfInspector
{
    public PdfInspection Inspect(Stream pdf) => new(Readable: true, Encrypted: false, PageCount: 1);

    /// <summary>Placeholder bytes that pass the %PDF- signature check.</summary>
    public static byte[] FakePdf => "%PDF-1.7 test"u8.ToArray();
}
