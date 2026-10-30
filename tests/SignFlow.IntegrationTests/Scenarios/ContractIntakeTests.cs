using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.Documents.Commands;
using SignFlow.Application.Features.Reviews;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// Rework spec R-M2-3 and R-M2-4: the upload stores the file and answers; a background job reads the text (by text
/// recognition when the PDF has almost none), then has Harvey review it. A scanned contract is reviewed on the
/// recognised words, and says so.
/// </summary>
public class ContractIntakeTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private const string Scanned = "ARTICLE 1 - OBJET Le présent contrat porte sur la maintenance. " +
                                   "ARTICLE 2 - RESPONSABILITÉ Le prestataire n'est responsable d'aucun dommage.";

    private sealed class PdfWithText(string text) : NullPdfService
    {
        public override Task<string> ExtractTextAsync(Stream pdfStream, CancellationToken cancellationToken = default) => Task.FromResult(text);
    }

    private sealed class FakeOcr(bool available, string text) : IOcrService
    {
        public int Calls { get; private set; }
        public bool IsAvailable => available;

        public Task<string> RecognizeAsync(Stream pdf, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(text);
        }
    }

    private static async Task<(ScenarioFixture Fixture, Document Document)> UploadedAsync(int pages = 2)
    {
        var fixture = new ScenarioFixture(OwnerId);
        var db = (ApplicationDbContext)fixture.Db;
        var document = Document.Create("Maintenance", "maintenance.pdf", "documents/maintenance.pdf", 10, pages, OwnerId);
        document.MarkIntakePending();
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return (fixture, document);
    }

    /// <summary>The intake with the real text provider, so the review reads what the intake stored.</summary>
    private static ContractIntake Intake(ScenarioFixture f, IPdfService pdf, IOcrService ocr)
    {
        var text = new ContractTextProvider(f.Db, f.GetHandler<IStorageService>(), pdf);
        var runner = new ReviewRunner(f.Db, text, f.GetHandler<IContractReviewService>(), f.GetHandler<INotificationHub>(),
            f.GetHandler<IAuditService>(), Options.Create(new ReviewOptions()), NullLogger<ReviewRunner>.Instance);
        return new ContractIntake(f.Db, f.GetHandler<IStorageService>(), pdf, ocr, f.GetHandler<IAiService>(), runner,
            NullLogger<ContractIntake>.Instance);
    }

    [Fact]
    public async Task The_upload_answers_at_once_and_queues_the_intake()
    {
        using var fixture = new ScenarioFixture(OwnerId);

        var result = await fixture.GetHandler<UploadDocumentCommandHandler>().HandleAsync(new UploadDocumentCommand(
            new MemoryStream(NullPdfInspector.FakePdf), "c.pdf", NullPdfInspector.FakePdf.Length, "application/pdf", "Contract"));

        Assert.Equal([(result.DocumentId, true)], fixture.ReviewQueue.Intakes);
        Assert.True((await fixture.Db.Documents.SingleAsync()).IntakePending);
        // Nothing reviews a contract whose text is not read yet.
        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.GetHandler<StartReviewCommandHandler>().HandleAsync(new StartReviewCommand(result.DocumentId)));
        Assert.Equal("review.intake_pending", refused.Code);
    }

    [Fact]
    public async Task A_pdf_with_its_own_text_is_read_without_ocr_and_reviewed()
    {
        var (fixture, document) = await UploadedAsync(pages: 1);
        using var _ = fixture;
        fixture.Harvey.Findings = [FakeHarvey.Finding("liability", "High", "Article 2 — Responsabilité")];
        var ocr = new FakeOcr(true, "never used");
        var embedded = Scanned + new string('.', 300);

        await Intake(fixture, new PdfWithText(embedded), ocr).RunAsync(document.Id, suggest: false);

        var stored = await fixture.Db.Documents.SingleAsync();
        Assert.Equal((ContractTextSource.Embedded, false, (string?)null), (stored.TextSource, stored.IntakePending, stored.OcrText));
        Assert.Equal(0, ocr.Calls);
        Assert.Equal(ReadinessVerdict.NeedsWork, stored.ReadinessVerdict);
        Assert.Equal(ReviewRunStatus.Completed, (await fixture.Db.ReviewRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_scan_is_read_by_text_recognition_and_reviewed_on_those_words()
    {
        var (fixture, document) = await UploadedAsync(pages: 2);
        using var _ = fixture;
        var ocr = new FakeOcr(true, Scanned);

        // Fewer than 200 characters per page of its own: a scan.
        await Intake(fixture, new PdfWithText("p. 1   p. 2"), ocr).RunAsync(document.Id, suggest: false);

        var stored = await fixture.Db.Documents.SingleAsync();
        Assert.Equal((ContractTextSource.Ocr, Scanned), (stored.TextSource, stored.OcrText));
        Assert.Contains(fixture.Harvey.ClausesSent, c => c.Text.Contains("n'est responsable d'aucun dommage"));
        var review = await fixture.GetHandler<GetLatestReviewQueryHandler>().HandleAsync(new GetLatestReviewQuery(document.Id));
        Assert.Equal((ContractTextSource.Ocr, false), (review.TextSource, review.IntakePending));
    }

    [Fact]
    public async Task A_scan_without_text_recognition_is_unreadable_and_its_review_fails_visibly()
    {
        var (fixture, document) = await UploadedAsync();
        using var _ = fixture;

        await Intake(fixture, new PdfWithText(""), new FakeOcr(false, "")).RunAsync(document.Id, suggest: false);

        var stored = await fixture.Db.Documents.SingleAsync();
        Assert.Equal((ContractTextSource.Unreadable, false), (stored.TextSource, stored.IntakePending));
        Assert.Equal(ReviewRun.NoText, (await fixture.Db.ReviewRuns.SingleAsync()).ErrorCode);
        Assert.Equal(ReadinessVerdict.NotEvaluated, stored.ReadinessVerdict);
    }

    [Fact]
    public void Text_recognition_is_off_when_its_tools_are_not_installed()
    {
        var ocr = new SignFlow.Infrastructure.Services.Pdf.TesseractOcrService(
            Options.Create(new SignFlow.Infrastructure.Services.Pdf.OcrSettings { TesseractPath = "no-such-tesseract-" + Guid.NewGuid().ToString("N") }),
            NullLogger<SignFlow.Infrastructure.Services.Pdf.TesseractOcrService>.Instance);

        Assert.False(ocr.IsAvailable);
    }

    [Fact]
    public async Task A_new_version_forgets_the_old_recognised_text_and_is_read_again()
    {
        var (fixture, document) = await UploadedAsync();
        using var _ = fixture;
        await Intake(fixture, new PdfWithText(""), new FakeOcr(true, Scanned)).RunAsync(document.Id, suggest: false);

        var handler = new UploadRevisionCommandHandler(
            fixture.Db, fixture.GetHandler<IStorageService>(), fixture.GetHandler<IPdfService>(), fixture.GetHandler<PdfUploadGuard>(),
            fixture.GetHandler<IReviewQueue>(), fixture.GetHandler<ICurrentUserService>(), fixture.GetHandler<IAuditService>(),
            NullLogger<UploadRevisionCommandHandler>.Instance);
        await handler.HandleAsync(new UploadRevisionCommand(
            document.Id, new MemoryStream(NullPdfInspector.FakePdf), "v2.pdf", NullPdfInspector.FakePdf.Length, "application/pdf", "v2"));

        var stored = await fixture.Db.Documents.SingleAsync();
        Assert.Equal((2, (string?)null, true), (stored.Version, stored.OcrText, stored.IntakePending));
        Assert.Contains((document.Id, false), fixture.ReviewQueue.Intakes);
    }
}
