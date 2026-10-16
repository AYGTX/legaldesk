using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.SignatureFields.Commands;
using SignFlow.Application.Features.Signing.Commands;
using SignFlow.Application.Features.Signing.Queries;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// A signature placeholder in an HTML template says where that signer's signature goes. The signing screen shows it
/// there — whose it is, and whether it is yours, still to come, or done — and the sealed PDF puts the signature image
/// at the same spot instead of only in a block at the end.
/// </summary>
public class HtmlSignaturePlacementTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private const string Html = "<p>Pour le Prestataire : {{providerSig}}</p><p>Pour le Client : {{clientSig}}</p><p>Fait à Tunis.</p>";

    private sealed class CapturingPdfGenerator : IHtmlTemplatePdfGenerator
    {
        public string? Html { get; private set; }
        public List<HtmlTemplatePdfSignature> Signatures { get; private set; } = [];

        public Task<Stream> GenerateAsync(string renderedHtmlContent, string title, List<HtmlTemplatePdfSignature> signatures, CancellationToken ct = default)
        {
            Html = renderedHtmlContent;
            Signatures = signatures;
            return Task.FromResult<Stream>(new MemoryStream());
        }
    }

    private sealed record Seed(ScenarioFixture Fixture, Document Document, Recipient Alice, Recipient Bob, SignatureField AliceField, SignatureField BobField);

    private static async Task<Seed> SeedAsync(bool withKeys = true)
    {
        var fixture = new ScenarioFixture(OwnerId);
        var db = (ApplicationDbContext)fixture.Db;

        db.Set<ApplicationUser>().Add(new ApplicationUser
        {
            Id = OwnerId, Email = "owner@test.com", UserName = "owner@test.com", FirstName = "Test", LastName = "Owner",
            IsActive = true, SecurityStamp = Guid.NewGuid().ToString()
        });

        var template = HtmlTemplate.Create("Contrat", Html, OwnerId);
        db.HtmlTemplates.Add(template);
        db.HtmlTemplateFields.AddRange(
            HtmlTemplateField.Create(template.Id, "providerSig", "Prestataire", HtmlTemplateFieldType.Signature, HtmlTemplateFieldRole.Approver, true, 1),
            HtmlTemplateField.Create(template.Id, "clientSig", "Client", HtmlTemplateFieldType.Signature, HtmlTemplateFieldRole.Approver, true, 2));

        var document = Document.Create("Contrat", "Contrat.html", $"html-templates/{template.Id}", 0, 1, OwnerId);
        document.SetHtmlSource(template.Id, JsonSerializer.Serialize(new Dictionary<string, string>()), Html);
        db.Documents.Add(document);

        var alice = Recipient.Create(document.Id, "Alice Provider", "alice@test.com", 1, ApprovalType.Signature);
        var bob = Recipient.Create(document.Id, "Bob Client", "bob@test.com", 2, ApprovalType.Signature);
        db.Recipients.AddRange(alice, bob);

        SignatureField Field(Recipient r, string key)
        {
            var f = SignatureField.Create(document.Id, FieldType.Signature, 1, 0, 0, 200, 80, r.Id);
            f.UpdateProperties(key, null, null, true, false, null, null, null);
            if (withKeys) f.SetTemplateKey(key);
            return f;
        }

        var aliceField = Field(alice, "providerSig");
        var bobField = Field(bob, "clientSig");
        db.SignatureFields.AddRange(aliceField, bobField);
        await db.SaveChangesAsync();

        await fixture.GetHandler<IWorkflowEngine>().StartAsync(document.Id);
        return new Seed(fixture, document, alice, bob, aliceField, bobField);
    }

    private static async Task SignAsync(ScenarioFixture f, Recipient who, SignatureField field)
    {
        // Acting on a request starts with confirming the document (and its review) was read.
        who.AcknowledgeReview();
        await f.Db.SaveChangesAsync();

        await f.GetHandler<SignFieldCommandHandler>().HandleAsync(new SignFieldCommand(
            SecureToken: who.SecureToken, FieldId: field.Id, SignatureType: SignatureType.Drawn,
            ImageBase64: "iVBORw0KGgo=", TypedText: null, FontFamily: null,
            IpAddress: "127.0.0.1", UserAgent: "test", Location: null));
    }

    private static ApproveSigningCommandHandler ApproveHandler(ScenarioFixture f, IHtmlTemplatePdfGenerator generator) => new(
        f.Db, f.GetHandler<IWorkflowEngine>(), f.GetHandler<IStorageService>(), f.GetHandler<IPdfStampService>(),
        generator, f.GetHandler<IAuditService>(), f.GetHandler<INotificationHub>(),
        NullLogger<ApproveSigningCommandHandler>.Instance);

    private static Task<SigningSessionDto> SessionAsync(ScenarioFixture f, Recipient who) =>
        new GetSigningSessionQueryHandler(f.Db).HandleAsync(new GetSigningSessionQuery(who.SecureToken));

    [Fact]
    public async Task The_signing_screen_shows_each_signature_where_the_template_put_it()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;

        var html = (await SessionAsync(fixture, seed.Alice)).RenderedHtmlContent!;

        // Alice's own slot is the one she signs; Bob's is still to come. Both sit in their own paragraph.
        Assert.Contains($"Pour le Prestataire : <span class=\"sig-slot sig-mine\" id=\"sig-slot-{seed.AliceField.Id}\">", html);
        Assert.Contains($"Pour le Client : <span class=\"sig-slot sig-waiting\" id=\"sig-slot-{seed.BobField.Id}\">", html);
        Assert.Contains("Alice Provider", html);
        Assert.Contains("Bob Client", html);
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public async Task A_signed_slot_says_who_signed_and_when()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;

        await SignAsync(fixture, seed.Alice, seed.AliceField);
        var html = (await SessionAsync(fixture, seed.Bob)).RenderedHtmlContent!;

        Assert.Contains("sig-slot sig-signed", html);
        Assert.Contains($"<span class=\"sig-date\">{DateTime.UtcNow:yyyy-MM-dd}</span>", html);
        Assert.Contains("sig-slot sig-mine", html);   // and Bob's own is now the one to click
    }

    [Fact]
    public async Task Hidden_identities_stay_hidden_in_the_slots()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;
        (await fixture.Db.Documents.SingleAsync()).SetHideRecipientIdentities(true);
        await fixture.Db.SaveChangesAsync();

        var html = (await SessionAsync(fixture, seed.Alice)).RenderedHtmlContent!;

        Assert.Contains("Alice Provider", html);          // her own name is no secret from her
        Assert.DoesNotContain("Bob Client", html);
        Assert.Contains("Recipient 2", html);
    }

    [Fact]
    public async Task The_sealed_pdf_is_built_with_each_signature_at_its_placeholder()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;
        var generator = new CapturingPdfGenerator();

        await SignAsync(fixture, seed.Alice, seed.AliceField);
        await ApproveHandler(fixture, generator).HandleAsync(new ApproveSigningCommand(seed.Alice.SecureToken, "127.0.0.1", "test"));
        await SignAsync(fixture, seed.Bob, seed.BobField);
        await ApproveHandler(fixture, generator).HandleAsync(new ApproveSigningCommand(seed.Bob.SecureToken, "127.0.0.1", "test"));

        // The last approval builds the PDF: the text carries a marker where each placeholder was...
        Assert.Contains($"Pour le Prestataire : {HtmlSignatureSlotsMarker("providerSig")}", generator.Html);
        Assert.Contains($"Pour le Client : {HtmlSignatureSlotsMarker("clientSig")}", generator.Html);

        // ...and each signature says which placeholder it belongs to.
        Assert.Equal(["providerSig", "clientSig"], generator.Signatures.Select(s => s.TemplateKey));
        Assert.Equal(["Alice Provider", "Bob Client"], generator.Signatures.Select(s => s.SignerName));
    }

    [Fact]
    public async Task A_contract_created_before_placeholders_were_linked_still_gets_its_signature_section()
    {
        var seed = await SeedAsync(withKeys: false);
        using var fixture = seed.Fixture;
        var generator = new CapturingPdfGenerator();

        await SignAsync(fixture, seed.Alice, seed.AliceField);
        await ApproveHandler(fixture, generator).HandleAsync(new ApproveSigningCommand(seed.Alice.SecureToken, "127.0.0.1", "test"));
        await SignAsync(fixture, seed.Bob, seed.BobField);
        await ApproveHandler(fixture, generator).HandleAsync(new ApproveSigningCommand(seed.Bob.SecureToken, "127.0.0.1", "test"));

        Assert.DoesNotContain("sf-signature", generator.Html);
        Assert.All(generator.Signatures, s => Assert.Null(s.TemplateKey));   // the generator appends these at the end
        Assert.Equal(2, generator.Signatures.Count);
    }

    private static string HtmlSignatureSlotsMarker(string key) => SignFlow.Application.Common.Services.HtmlSignatureSlots.Marker(key);
}
