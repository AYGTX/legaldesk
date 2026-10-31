using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SignFlow.Api.Controllers.V1;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.Documents.Commands;
using SignFlow.Application.Features.Documents.Queries;
using SignFlow.Application.Features.Signing;
using SignFlow.Application.Features.Signing.Commands;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// Rework spec M5, access and evidence (R-M5-1 to R-M5-7): the link is stored as a hash and shown to nobody; it opens
/// the page and sends a code, the code opens a session, and only a session acts. Refusals carry a code. Every signature
/// records who proved what on which file, a drawn signature is re-encoded, and a date is the server's.
/// </summary>
public class SigningAccessTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private const string Alice = "alice@partner.com";
    private const string Bob = "bob@partner.com";

    private sealed record Sent(ScenarioFixture Fixture, Document Document, Recipient Alice, Recipient Bob, SignatureField AliceField,
        SignatureField AliceDate, string AliceLink);

    /// <summary>A contract out for signature: Alice signs first, then Bob. Returns Alice's e-mailed link.</summary>
    private static async Task<Sent> SentAsync()
    {
        var fixture = new ScenarioFixture(OwnerId);
        var db = (ApplicationDbContext)fixture.Db;
        db.Set<ApplicationUser>().Add(new ApplicationUser
        {
            Id = OwnerId, Email = "owner@test.com", UserName = "owner@test.com", FirstName = "Test", LastName = "Owner",
            IsActive = true, SecurityStamp = Guid.NewGuid().ToString()
        });
        var document = Document.Create("Supply agreement", "supply.pdf", "documents/supply.pdf", 10, 1, OwnerId);
        document.SetHash("ABCDEF0123");
        db.Documents.Add(document);
        var alice = Recipient.Create(document.Id, "Alice", Alice, 1, ApprovalType.Signature);
        var bob = Recipient.Create(document.Id, "Bob", Bob, 2, ApprovalType.Signature);
        db.Recipients.AddRange(alice, bob);
        var aliceField = SignatureField.Create(document.Id, FieldType.Signature, 1, 10, 10, 100, 40, alice.Id);
        var aliceDate = SignatureField.Create(document.Id, FieldType.Date, 1, 10, 60, 100, 20, alice.Id);
        db.SignatureFields.AddRange(aliceField, aliceDate, SignatureField.Create(document.Id, FieldType.Signature, 1, 10, 100, 100, 40, bob.Id));
        await db.SaveChangesAsync();

        await fixture.ReviewAsync(document.Id);
        await fixture.GetHandler<SendDocumentCommandHandler>().HandleAsync(new SendDocumentCommand(document.Id));

        var link = Email(fixture).Last(Alice, "DocumentReceived", "SigningLink")["/sign/".Length..];
        return new Sent(fixture, document, alice, bob, aliceField, aliceDate, link);
    }

    private static NullEmailService Email(ScenarioFixture f) => (NullEmailService)f.GetHandler<IEmailService>();
    private static ISigningAccess Access(ScenarioFixture f) => f.GetHandler<ISigningAccess>();

    private static async Task<OpenedSigningSession> VerifiedAsync(ScenarioFixture f, string link, string email)
    {
        await Access(f).SendCodeAsync(link);
        return await Access(f).VerifyCodeAsync(link, Email(f).Last(email, "SigningCode", "Code"), "203.0.113.7");
    }

    private static async Task<SigningRefusedException> Refused(Func<Task> act) => await Assert.ThrowsAsync<SigningRefusedException>(act);

    // ------------------------------------------------------------------ the link

    [Fact]
    public async Task The_link_is_stored_as_a_hash_and_no_response_carries_a_token()
    {
        var s = await SentAsync();
        using var _ = s.Fixture;

        var stored = await s.Fixture.Db.Recipients.SingleAsync(r => r.Id == s.Alice.Id);
        Assert.Equal(SecureTokens.Hash(s.AliceLink), stored.TokenHash);
        Assert.Equal(43, s.AliceLink.Length);                             // 32 random bytes, base64url
        Assert.Null((await s.Fixture.Db.Recipients.SingleAsync(r => r.Id == s.Bob.Id)).TokenHash); // not asked yet

        // Neither the contract detail nor "my requests" has anything that looks like a token, for any role.
        foreach (var type in new[] { typeof(RecipientDto), typeof(SignFlow.Application.Features.Requests.Queries.ReceivedRequestDto) })
            Assert.DoesNotContain(type.GetProperties(), p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_link_alone_shows_who_asks_but_neither_reads_nor_acts()
    {
        var s = await SentAsync();
        using var _ = s.Fixture;

        var intro = await Access(s.Fixture).IntroAsync(s.AliceLink);
        Assert.Equal(("Supply agreement", "Test Owner", "Alice"), (intro.DocumentTitle, intro.SenderName, intro.ParticipantName));
        Assert.Equal("ali***@partner.com", intro.MaskedEmail);

        Assert.Equal("session.required", (await Refused(() => Access(s.Fixture).ResolveAsync(s.AliceLink, null, SigningNeed.View))).Code);
        Assert.Equal("session.required", (await Refused(() => Access(s.Fixture).ResolveAsync(s.AliceLink, null, SigningNeed.Act))).Code);
        Assert.Equal("link.invalid", (await Refused(() => Access(s.Fixture).IntroAsync("not-a-link"))).Code);
    }

    [Fact]
    public async Task An_expired_link_is_refused_with_410_for_every_need()
    {
        var s = await SentAsync();
        using var _ = s.Fixture;
        var session = await VerifiedAsync(s.Fixture, s.AliceLink, Alice);

        var recipient = await s.Fixture.Db.Recipients.SingleAsync(r => r.Id == s.Alice.Id);
        typeof(Recipient).GetProperty(nameof(Recipient.TokenExpiresAt))!.SetValue(recipient, DateTime.UtcNow.AddMinutes(-1));
        await s.Fixture.Db.SaveChangesAsync();

        foreach (var need in Enum.GetValues<SigningNeed>())
        {
            var refused = await Refused(() => Access(s.Fixture).ResolveAsync(s.AliceLink, need == SigningNeed.Link ? null : session.Secret, need));
            Assert.Equal(("link.expired", 410), (refused.Code, refused.Status));
        }
        Assert.Equal("link.expired", (await Refused(() => Access(s.Fixture).SendCodeAsync(s.AliceLink))).Code);
    }

    [Fact]
    public void Every_signing_endpoint_takes_the_link_and_goes_through_the_gate()
    {
        // The controller has no handler that could be reached without ISigningAccess: each action is keyed by the link.
        var actions = typeof(SigningController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().Any(a => a is Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute))
            .ToList();
        Assert.True(actions.Count >= 14);
        Assert.All(actions, a => Assert.Contains(a.GetParameters(), p => p.Name == "token"));
        Assert.Contains(typeof(SigningController).GetConstructors().Single().GetParameters(), p => p.ParameterType == typeof(ISigningAccess));
    }

    // ------------------------------------------------------------------ the code and the session

    [Fact]
    public async Task A_wrong_code_counts_and_five_end_it()
    {
        var s = await SentAsync();
        using var _ = s.Fixture;
        await Access(s.Fixture).SendCodeAsync(s.AliceLink);

        var first = await Refused(() => Access(s.Fixture).VerifyCodeAsync(s.AliceLink, "000000x", null));
        Assert.Equal("code.invalid", first.Code);
        Assert.Contains("4", first.Message);
        for (var i = 0; i < 3; i++)
            await Refused(() => Access(s.Fixture).VerifyCodeAsync(s.AliceLink, "nope", null));
        Assert.Equal("code.expired", (await Refused(() => Access(s.Fixture).VerifyCodeAsync(s.AliceLink, "nope", null))).Code);

        // Even the right code no longer works: a new one is needed.
        var code = Email(s.Fixture).Last(Alice, "SigningCode", "Code");
        Assert.Equal("code.expired", (await Refused(() => Access(s.Fixture).VerifyCodeAsync(s.AliceLink, code, null))).Code);
        Assert.Equal("code.too_soon", (await Refused(() => Access(s.Fixture).SendCodeAsync(s.AliceLink))).Code);
    }

    [Fact]
    public async Task The_code_opens_a_session_that_acts_and_records_the_verification()
    {
        var s = await SentAsync();
        using var _ = s.Fixture;

        var session = await VerifiedAsync(s.Fixture, s.AliceLink, Alice);
        Assert.True(session.ExpiresAt > DateTime.UtcNow.AddMinutes(29));

        var context = await Access(s.Fixture).ResolveAsync(s.AliceLink, session.Secret, SigningNeed.Act);
        Assert.Equal((s.Alice.Id, Alice, SigningSession.EmailCode), (context.Recipient.Id, context.Session!.VerifiedEmail, context.Session.Method));
        var stored = await s.Fixture.Db.Recipients.SingleAsync(r => r.Id == s.Alice.Id);
        Assert.Equal(SigningSession.EmailCode, stored.VerificationMethod);
        Assert.NotNull(stored.VerifiedAt);

        // The secret is kept only as a hash, and a made-up one is refused.
        Assert.DoesNotContain(await s.Fixture.Db.SigningSessions.ToListAsync(), x => x.SecretHash == session.Secret);
        Assert.Equal("session.expired", (await Refused(() => Access(s.Fixture).ResolveAsync(s.AliceLink, "made-up", SigningNeed.View))).Code);
    }

    [Fact]
    public async Task Not_your_turn_and_already_done_are_refused_with_their_codes()
    {
        var s = await SentAsync();
        using var _ = s.Fixture;

        // Bob has no link until his turn; a session opened from his account still waits for Alice.
        var user = (FakeCurrentUserService)s.Fixture.GetHandler<ICurrentUserService>();
        user.EmailOverride = Bob;
        var bob = await Access(s.Fixture).OpenForAccountAsync(s.Bob.Id, null);
        Assert.Equal("step.not_open", (await Refused(() => Access(s.Fixture).ResolveAsync(SigningAccess.SessionRoute, bob.Secret, SigningNeed.Act))).Code);
        await Access(s.Fixture).ResolveAsync(SigningAccess.SessionRoute, bob.Secret, SigningNeed.View); // reading is fine

        // Someone else's account cannot open Alice's session.
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Access(s.Fixture).OpenForAccountAsync(s.Alice.Id, null));

        var alice = await VerifiedAsync(s.Fixture, s.AliceLink, Alice);
        await s.Fixture.GetHandler<RejectDocumentHandlerAccessor>().Decline(s.Alice.Id);
        Assert.Contains((await Refused(() => Access(s.Fixture).ResolveAsync(s.AliceLink, alice.Secret, SigningNeed.Act))).Code,
            new[] { "participant.done", "contract.closed" });
    }

    // ------------------------------------------------------------------ evidence

    [Fact]
    public async Task A_signature_records_the_verified_email_the_file_and_the_image_and_a_date_is_the_servers()
    {
        var s = await SentAsync();
        using var _ = s.Fixture;
        var session = await VerifiedAsync(s.Fixture, s.AliceLink, Alice);
        var context = await Access(s.Fixture).ResolveAsync(s.AliceLink, session.Secret, SigningNeed.Act);
        var evidence = new SigningEvidence(context.Session!.VerifiedEmail, context.Session.Method, context.Session.VerifiedAt);
        var sign = s.Fixture.GetHandler<SignFieldCommandHandler>();
        await new AcknowledgeReviewCommandHandler(s.Fixture.Db, s.Fixture.GetHandler<IAuditService>())
            .HandleAsync(new AcknowledgeReviewCommand(s.Alice.Id, "203.0.113.7", "test-agent"));

        // A PNG with a text chunk: stored without it.
        var png = SignatureImage.Encode(4, 2, Enumerable.Repeat((byte)200, 32).ToArray());
        await sign.HandleAsync(new SignFieldCommand(s.Alice.Id, s.AliceField.Id, SignatureType.Drawn, Convert.ToBase64String(png), null, null,
            "203.0.113.7", "test-agent", evidence));
        // The signer's browser says a date; the server writes its own.
        await sign.HandleAsync(new SignFieldCommand(s.Alice.Id, s.AliceDate.Id, SignatureType.Typed, null, "01/01/1999", null,
            "203.0.113.7", "test-agent", evidence));

        var signatures = await s.Fixture.Db.Signatures.ToListAsync();
        var drawn = signatures.Single(x => x.SignatureFieldId == s.AliceField.Id);
        Assert.Equal((Alice, SigningSession.EmailCode, 1, "abcdef0123"), (drawn.VerifiedEmail, drawn.VerificationMethod, drawn.DocumentVersion, drawn.DocumentSha256));
        Assert.Equal(64, drawn.ImageSha256!.Length);
        Assert.Equal("203.0.113.7", drawn.Ip);

        var date = await s.Fixture.Db.SignatureFields.SingleAsync(f => f.Id == s.AliceDate.Id);
        Assert.Equal(SigningDates.Today("Africa/Tunis", "dd/MM/yyyy"), date.SignedValue);
    }

    [Fact]
    public void A_drawn_signature_must_be_a_small_png_and_is_written_again_without_extras()
    {
        Assert.Throws<ValidationException>(() => SignatureImage.Sanitize(Convert.ToBase64String("GIF89a..."u8.ToArray())));
        Assert.Throws<ValidationException>(() => SignatureImage.Sanitize(Convert.ToBase64String(SignatureImage.Encode(1300, 10, new byte[1300 * 10 * 4]))));
        Assert.Throws<ValidationException>(() => SignatureImage.Sanitize(Convert.ToBase64String(new byte[SignatureImage.MaxBytes + 1])));

        // A PNG with a tEXt chunk (metadata) comes back as pixels only, and still decodes to the same size.
        var clean = SignatureImage.Encode(3, 2, Enumerable.Range(0, 24).Select(i => (byte)i).ToArray());
        var withText = clean[..33].Concat(TextChunk("Comment", "<script>")).Concat(clean[33..]).ToArray();
        var (png, sha) = SignatureImage.Sanitize("data:image/png;base64," + Convert.ToBase64String(withText));
        Assert.Equal(clean, png);
        Assert.Equal(64, sha.Length);
        Assert.DoesNotContain("script", System.Text.Encoding.ASCII.GetString(png));
    }

    private static byte[] TextChunk(string key, string value)
    {
        var body = System.Text.Encoding.ASCII.GetBytes(key + "\0" + value);
        var length = BitConverter.GetBytes(body.Length).Reverse().ToArray();
        return [.. length, .. "tEXt"u8.ToArray(), .. body, 0, 0, 0, 0];   // the CRC is not checked on chunks that are dropped
    }
}

/// <summary>Declines as a participant would, through the real handler.</summary>
public sealed class RejectDocumentHandlerAccessor(ICommandHandler<RejectDocumentCommand> handler)
{
    public Task Decline(Guid recipientId) => handler.HandleAsync(new RejectDocumentCommand(recipientId, "Not this version."));
}
