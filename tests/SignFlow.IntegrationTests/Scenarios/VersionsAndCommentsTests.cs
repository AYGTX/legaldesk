using System.IO.Compression;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.Comments.Commands;
using SignFlow.Application.Features.Comments.Queries;
using SignFlow.Application.Features.Documents.Commands;
using SignFlow.Application.Features.Documents.Queries;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// Versions and discussion (M7): internal notes participants never see, mentions picked from people who may read the
/// thread, what each version was (R-M7-4), Word revisions (R-M7-3) and the rounds kept across revisions (R-M7-2).
/// </summary>
public class VersionsAndCommentsTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private const string OwnerEmail = "owner@test.com";

    private static async Task<ScenarioFixture> FixtureAsync()
    {
        var f = new ScenarioFixture(OwnerId, OwnerEmail);
        await AddUserAsync(f, "Owner", OwnerEmail, OwnerId);
        return f;
    }

    private static async Task<ApplicationUser> AddUserAsync(ScenarioFixture f, string first, string email, Guid? id = null)
    {
        var user = new ApplicationUser
        {
            Id = id ?? Guid.NewGuid(), FirstName = first, LastName = "Test", UserName = email, Email = email,
            IsActive = true, EmailConfirmed = true
        };
        Assert.True((await f.GetHandler<UserManager<ApplicationUser>>().CreateAsync(user)).Succeeded);
        return user;
    }

    private static FakeCurrentUserService Me(ScenarioFixture f) => (FakeCurrentUserService)f.GetHandler<ICurrentUserService>();

    private static async Task<(Document Document, Recipient Ali)> ContractAsync(ScenarioFixture f)
    {
        var document = Document.Create("Contrat de services", "contrat.pdf", "documents/contrat.pdf", 1000, 1, OwnerId);
        document.SetHash("ABC123");
        f.Db.Documents.Add(document);
        var ali = Recipient.Create(document.Id, "Ali", "ali@client.tn", 1, ApprovalType.Approval);
        f.Db.Recipients.Add(ali);
        await f.Db.SaveChangesAsync();
        return (document, ali);
    }

    private static AddCommentCommandHandler Comments(ScenarioFixture f) => new(
        f.Db, f.GetHandler<ICurrentUserService>(), f.GetHandler<IAuditService>(), f.GetHandler<INotificationService>(),
        f.GetHandler<INotificationHub>(), NullLogger<AddCommentCommandHandler>.Instance);

    private static AddSigningCommentCommandHandler SigningComments(ScenarioFixture f) => new(
        f.Db, f.GetHandler<IAuditService>(), f.GetHandler<INotificationService>(), f.GetHandler<INotificationHub>(),
        NullLogger<AddSigningCommentCommandHandler>.Instance);

    private static UploadRevisionCommandHandler Revisions(ScenarioFixture f, IDocxConverter? docx = null) => new(
        f.Db, f.GetHandler<IStorageService>(), f.GetHandler<IPdfService>(), f.GetHandler<PdfUploadGuard>(),
        f.GetHandler<IReviewQueue>(), f.GetHandler<ICurrentUserService>(), f.GetHandler<IAuditService>(),
        NullLogger<UploadRevisionCommandHandler>.Instance, f.GetHandler<IContractTextProvider>(), docx);

    private static AddCommentCommand Note(Guid documentId, string text, CommentVisibility visibility, List<Guid>? mentions = null, Guid? parent = null) =>
        new(documentId, text, parent, null, null, null, "127.0.0.1", visibility, mentions);

    [Fact]
    public async Task An_internal_note_is_never_shown_to_a_participant_nor_answerable_by_one()
    {
        using var f = await FixtureAsync();
        var (document, ali) = await ContractAsync(f);
        var note = await Comments(f).HandleAsync(Note(document.Id, "Négocier le plafond à la baisse", CommentVisibility.Internal));
        var shared = await Comments(f).HandleAsync(Note(document.Id, "Merci de relire l'article 5", CommentVisibility.Participants));
        Assert.Equal(1, note.VersionNumber);

        // On the signing page.
        var seen = await new GetSigningCommentsQueryHandler(f.Db).HandleAsync(new GetSigningCommentsQuery(ali.Id));
        Assert.Equal([shared.Id], seen.Select(c => c.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => SigningComments(f).HandleAsync(
            new AddSigningCommentCommand(ali.Id, "Pourquoi ?", note.Id, null, null, null, "127.0.0.1")));

        // Signed in as the participant, in the app.
        await AddUserAsync(f, "Ali", "ali@client.tn");
        Me(f).EmailOverride = "ali@client.tn";
        Me(f).UserIdOverride = (await f.Db.Users.FirstAsync(u => u.Email == "ali@client.tn")).Id;
        Me(f).RolesOverride = [];
        var visible = await new GetDocumentCommentsQueryHandler(f.Db, f.GetHandler<ICurrentUserService>())
            .HandleAsync(new GetDocumentCommentsQuery(document.Id));
        Assert.DoesNotContain(visible, c => c.Id == note.Id);
        Assert.Contains(visible, c => c.Id == shared.Id);

        // A participant's comment is always shared, whatever they ask for.
        var own = await Comments(f).HandleAsync(Note(document.Id, "D'accord", CommentVisibility.Internal));
        Assert.Equal(CommentVisibility.Participants, own.Visibility);
    }

    [Fact]
    public async Task Mentions_are_people_picked_from_the_list_and_a_participant_cannot_be_mentioned_in_an_internal_note()
    {
        using var f = await FixtureAsync();
        var (document, ali) = await ContractAsync(f);
        var colleague = await AddUserAsync(f, "Sami", "sami@legaldesk.tn");

        var ok = await Comments(f).HandleAsync(Note(document.Id, "Sami, Ali : votre avis ?", CommentVisibility.Participants, [colleague.Id, ali.Id]));
        Assert.NotNull(ok);
        var stored = await f.Db.Comments.SingleAsync(c => c.Id == ok.Id);
        Assert.Equal(2, stored.Mentioned.Count);

        await Assert.ThrowsAsync<ValidationException>(() =>
            Comments(f).HandleAsync(Note(document.Id, "Entre nous", CommentVisibility.Internal, [ali.Id])));
        await Assert.ThrowsAsync<ValidationException>(() =>
            Comments(f).HandleAsync(Note(document.Id, "Quelqu'un d'autre", CommentVisibility.Participants, [Guid.NewGuid()])));
    }

    [Fact]
    public async Task A_revision_keeps_what_the_outgoing_version_was_and_resets_the_verdict()
    {
        using var f = await FixtureAsync();
        var (document, _) = await ContractAsync(f);
        var engine = f.GetHandler<IWorkflowEngine>();
        await engine.StartAsync(document.Id);
        await engine.RequestChangesAsync(document.Id, (await f.Db.Recipients.FirstAsync()).Id, "Plafond trop bas");

        using var pdf = new MemoryStream(NullPdfInspector.FakePdf);
        var result = await Revisions(f).HandleAsync(new UploadRevisionCommand(
            document.Id, pdf, "contrat-v2.pdf", pdf.Length, "application/pdf", "Plafond relevé"));

        Assert.Equal(2, result.Version);
        var v1 = await f.Db.DocumentVersions.SingleAsync(v => v.DocumentId == document.Id);
        var round1 = await f.Db.ContractRounds.SingleAsync(r => r.DocumentId == document.Id && r.Number == 1);
        Assert.Equal(1, v1.VersionNumber);
        Assert.Equal("abc123", v1.FileSha256);
        Assert.Equal(VersionFingerprints.TextSha256(f.Text.Text), v1.TextSha256);
        Assert.Equal(64, v1.TextSha256!.Length);
        Assert.Equal(ContractTextSource.Embedded, v1.TextSource);
        Assert.Equal(round1.Id, v1.RoundId);
        Assert.Equal(ReadinessVerdict.NotEvaluated, (await f.Db.Documents.SingleAsync(d => d.Id == document.Id)).ReadinessVerdict);

        var rounds = await new GetDocumentRoundsQueryHandler(f.Db, f.GetHandler<ICurrentUserService>())
            .HandleAsync(new GetDocumentRoundsQuery(document.Id));
        var only = Assert.Single(rounds);
        Assert.Equal(RoundOutcome.ChangesRequested, only.Outcome);
        Assert.Equal(WorkflowStepStatus.ChangesRequested, Assert.Single(only.Steps).Status);
    }

    [Fact]
    public async Task A_Word_revision_is_converted_to_the_PDF_of_the_next_version()
    {
        using var f = await FixtureAsync();
        var (document, _) = await ContractAsync(f);
        var docx = new MemoryStream(DocxRoundTrip.Write("Contrat de services", "Article 1\nObjet\n\nArticle 2\nPrix : 10 000 TND"));

        // No converter on this server: a clear refusal, not a broken version.
        var refused = await Assert.ThrowsAsync<ConflictException>(() => Revisions(f).HandleAsync(new UploadRevisionCommand(
            document.Id, docx, "contrat.docx", docx.Length, DocxRoundTrip.ContentType, null)));
        Assert.Equal("revision.docx_unavailable", refused.Code);

        docx.Position = 0;
        var converter = new FakeConverter();
        var result = await Revisions(f, converter).HandleAsync(new UploadRevisionCommand(
            document.Id, docx, "contrat.docx", docx.Length, "application/octet-stream", "Prix revu"));

        Assert.Equal(2, result.Version);
        Assert.Equal(1, converter.Calls);
        var updated = await f.Db.Documents.SingleAsync(d => d.Id == document.Id);
        Assert.Equal("contrat.pdf", updated.FileName);
        Assert.EndsWith(".pdf", updated.StoragePath);

        // Anything that is not a Word file is refused before the converter runs.
        using var fake = new MemoryStream("not a zip"u8.ToArray());
        await Assert.ThrowsAsync<ValidationException>(() => Revisions(f, converter).HandleAsync(new UploadRevisionCommand(
            document.Id, fake, "contrat.docx", fake.Length, DocxRoundTrip.ContentType, null)));
        Assert.Equal(1, converter.Calls);
    }

    [Fact]
    public void The_Word_download_holds_the_contract_text_one_paragraph_per_line()
    {
        var bytes = DocxRoundTrip.Write("Contrat & annexes", "Article 1 – Objet\n\n\n\nLe prix est < 10 000 TND.\u0001");

        using var stream = new MemoryStream(bytes);
        Assert.True(DocxRoundTrip.LooksLikeDocx(stream));
        using var zip = new ZipArchive(stream);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = reader.ReadToEnd();
        var parsed = System.Xml.Linq.XDocument.Parse(xml); // well-formed despite &, < and a control character
        var paragraphs = parsed.Descendants().Where(e => e.Name.LocalName == "p").Select(p => p.Value).ToList();
        Assert.Equal(["Contrat & annexes", "Article 1 – Objet", "", "Le prix est < 10 000 TND."], paragraphs);
    }

    private sealed class FakeConverter : IDocxConverter
    {
        public int Calls { get; private set; }

        public Task<Stream> ToPdfAsync(Stream docx, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<Stream>(new MemoryStream(NullPdfInspector.FakePdf));
        }
    }
}
