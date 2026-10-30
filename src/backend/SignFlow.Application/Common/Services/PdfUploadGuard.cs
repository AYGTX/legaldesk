using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Common.Services;

/// <summary>What a structural look at a PDF found, before anything else reads it.</summary>
public record PdfInspection(bool Readable, bool Encrypted, int PageCount);

/// <summary>Opens a file as a PDF without trusting it (PdfPig in production).</summary>
public interface IPdfInspector
{
    PdfInspection Inspect(Stream pdf);
}

/// <summary>The virus scanner could not be reached; the upload is refused rather than accepted unscanned (R-M2-2).</summary>
public class ScannerUnavailableException(string message) : Exception(message)
{
    public const string Code = "upload.scannerUnavailable";
}

/// <summary>
/// Every PDF that enters LegalDesk passes here first (R-M2-1, R-M2-2): at most 50 MB, starts with %PDF-, opens as a
/// PDF, is not encrypted, and is clean according to the virus scanner. Each refusal says why, in a field error the
/// web app can show.
/// </summary>
public class PdfUploadGuard(IPdfInspector inspector, IVirusScanService scanner, ILogger<PdfUploadGuard> logger)
{
    public const long MaxBytes = 50L * 1024 * 1024;
    private static readonly byte[] Signature = "%PDF-"u8.ToArray();

    /// <summary>Checks the file and leaves the stream at position 0. Returns the page count.</summary>
    public async Task<int> EnsureSafeAsync(Stream file, string fileName, CancellationToken cancellationToken = default)
    {
        if (!file.CanSeek)
            throw new InvalidOperationException("Uploads must be buffered before they are checked.");
        if (file.Length > MaxBytes)
            throw Refuse("The file is larger than 50 MB.");
        if (file.Length < Signature.Length)
            throw Refuse("The file is empty or is not a PDF.");

        file.Position = 0;
        var head = new byte[Signature.Length];
        await file.ReadExactlyAsync(head, cancellationToken);
        if (!head.AsSpan().SequenceEqual(Signature))
            throw Refuse("The file is not a PDF.");

        file.Position = 0;
        var inspection = inspector.Inspect(file);
        if (inspection.Encrypted)
            throw Refuse("The PDF is password-protected. Remove the protection and upload it again.");
        if (!inspection.Readable || inspection.PageCount < 1)
            throw Refuse("The PDF is damaged and cannot be read.");

        file.Position = 0;
        var scan = await scanner.ScanAsync(file, fileName, cancellationToken);
        file.Position = 0;
        if (!scan.IsSafe)
        {
            logger.LogWarning("Upload {FileName} refused by the virus scanner: {Threat}", fileName, scan.ThreatName);
            throw Refuse("The file was flagged by the virus scanner and was not accepted.");
        }

        return inspection.PageCount;
    }

    private static ValidationException Refuse(string message) =>
        new([new FluentValidation.Results.ValidationFailure("file", message)]);
}
