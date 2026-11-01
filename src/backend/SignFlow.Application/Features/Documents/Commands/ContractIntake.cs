using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Reviews;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

public interface IContractIntake
{
    /// <param name="suggest">Propose the obligation date and the contract term (a first upload, not a revision).</param>
    Task RunAsync(Guid documentId, bool suggest, CancellationToken cancellationToken = default);
}

/// <summary>
/// What happens to a new contract file after the upload has answered (R-M2-3): read its text, by text recognition when
/// the PDF has almost none of its own (R-M2-4), propose its dates, then have Harvey review it. One background job, so
/// the upload returns at once and a scanned 40-page contract does not hold a request open.
/// </summary>
public class ContractIntake(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    IOcrService ocr,
    IAiService aiService,
    IReviewRunner reviewRunner,
    ILogger<ContractIntake> logger) : IContractIntake
{
    /// <summary>Below this many characters of text per page on average, the PDF is treated as a scan.</summary>
    public const int MinCharsPerPage = 200;

    public async Task RunAsync(Guid documentId, bool suggest, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && !d.IsDeleted, cancellationToken);
        if (document is null) return;

        string text;
        try
        {
            text = await ReadAsync(document, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The file could not be read (storage down, say): the owner can still start the review by hand.
            logger.LogError(ex, "Contract {DocumentId} could not be read at intake", document.Id);
            document.CompleteIntake(ContractTextSource.Embedded, null);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }
        await db.SaveChangesAsync(cancellationToken);

        if (suggest && !string.IsNullOrWhiteSpace(text))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // Every new file is reviewed: the owner opens the contract on Harvey's findings, not on a button. One review per
        // version at a time: if someone already started one, it is left to finish.
        var active = await db.ReviewRuns.AnyAsync(r => r.DocumentId == document.Id && r.VersionNumber == document.Version
            && (r.Status == ReviewRunStatus.Queued || r.Status == ReviewRunStatus.Running), cancellationToken);
        if (active) return;

        var run = ReviewRun.Queue(document.Id, document.Version, document.OwnerId, DateTime.UtcNow);
        db.ReviewRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        await reviewRunner.RunAsync(run.Id, cancellationToken);
    }

    private async Task<string> ReadAsync(Document document, CancellationToken ct)
    {
        await using var pdf = await storage.DownloadAsync(document.StoragePath, ct);
        using var buffer = new MemoryStream();
        await pdf.CopyToAsync(buffer, ct);

        buffer.Position = 0;
        var text = await pdfService.ExtractTextAsync(buffer, ct);
        var perPage = text.Count(c => !char.IsWhiteSpace(c)) / Math.Max(1, document.PageCount);
        if (perPage >= MinCharsPerPage)
        {
            document.CompleteIntake(ContractTextSource.Embedded, null);
            return text;
        }

        if (!ocr.IsAvailable)
        {
            logger.LogWarning("Contract {DocumentId} looks scanned ({PerPage} characters per page) and no text recognition is installed",
                document.Id, perPage);
            document.CompleteIntake(string.IsNullOrWhiteSpace(text) ? ContractTextSource.Unreadable : ContractTextSource.Embedded, null);
            return text;
        }

        try
        {
            buffer.Position = 0;
            var recognised = await ocr.RecognizeAsync(buffer, ct);
            if (!string.IsNullOrWhiteSpace(recognised))
            {
                document.CompleteIntake(ContractTextSource.Ocr, recognised);
                return recognised;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Text recognition failed on contract {DocumentId}", document.Id);
        }

        document.CompleteIntake(string.IsNullOrWhiteSpace(text) ? ContractTextSource.Unreadable : ContractTextSource.Embedded, null);
        return text;
    }

}
