using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
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
/// Before a participant signs, approves or reviews, they have to have looked at the document and — if they are one of
/// the organisation's own — at what the AI found in it. The signing screen asks for that confirmation; these tests
/// check that the server holds the line for anyone who skips the screen.
/// </summary>
public class ReviewGateTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private sealed record Seed(ScenarioFixture Fixture, Document Document, Recipient Signer, SignatureField Field, Recipient Outsider);

    private static async Task<Seed> SeedAsync(bool reviewed = true)
    {
        var fixture = new ScenarioFixture(OwnerId);
        var db = (ApplicationDbContext)fixture.Db;

        db.Set<ApplicationUser>().AddRange(
            new ApplicationUser { Id = OwnerId, Email = "owner@test.com", UserName = "owner@test.com", FirstName = "Test", LastName = "Owner", IsActive = true, SecurityStamp = Guid.NewGuid().ToString() },
            // A member of the organisation: one of the participants below has this address.
            new ApplicationUser { Id = Guid.NewGuid(), Email = "Member@Test.com", UserName = "member@test.com", FirstName = "Mia", LastName = "Member", IsActive = true, SecurityStamp = Guid.NewGuid().ToString() });

        var document = Document.Create("Supply agreement", "supply.pdf", "documents/supply.pdf", 10, 1, OwnerId);
        db.Documents.Add(document);

        var signer = Recipient.Create(document.Id, "Mia Member", "member@test.com", 1, ApprovalType.Signature);
        var outsider = Recipient.Create(document.Id, "Oscar Outside", "oscar@partner.com", 2, ApprovalType.Signature);
        db.Recipients.AddRange(signer, outsider);
        var field = SignatureField.Create(document.Id, FieldType.Signature, 1, 0, 0, 200, 80, signer.Id);
        db.SignatureFields.Add(field);
        await db.SaveChangesAsync();

        if (reviewed)
        {
            // A real review run: one open High and one open Low finding from Harvey.
            fixture.Harvey.Findings =
            [
                FakeHarvey.Finding("liability", "High", "Article 2 — Responsabilité"),
                FakeHarvey.Finding("notices", "Low", "Article 3 — Durée"),
            ];
            await fixture.ReviewAsync(document.Id);
        }

        await fixture.GetHandler<IWorkflowEngine>().StartAsync(document.Id);
        return new Seed(fixture, document, signer, field, outsider);
    }

    private static Task<SignFieldResult> Sign(ScenarioFixture f, Recipient who, SignatureField field) =>
        f.GetHandler<SignFieldCommandHandler>().HandleAsync(new SignFieldCommand(
            who.SecureToken, field.Id, SignatureType.Typed, null, "Mia", null, "127.0.0.1", "test", null));

    private static AcknowledgeReviewCommandHandler Acknowledge(ScenarioFixture f) => new(f.Db, f.GetHandler<IAuditService>());

    private static Task<SigningSessionDto> Session(ScenarioFixture f, Recipient who) =>
        new GetSigningSessionQueryHandler(f.Db).HandleAsync(new GetSigningSessionQuery(who.SecureToken));

    [Fact]
    public async Task Signing_is_refused_until_the_participant_confirms_they_read_it()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;

        var refused = await Assert.ThrowsAsync<ConflictException>(() => Sign(fixture, seed.Signer, seed.Field));
        Assert.Contains("confirm", refused.Message);
        Assert.False((await fixture.Db.SignatureFields.SingleAsync()).IsSigned);

        await Acknowledge(fixture).HandleAsync(new AcknowledgeReviewCommand(seed.Signer.SecureToken, "127.0.0.1", "test"));

        Assert.True((await Sign(fixture, seed.Signer, seed.Field)).AllFieldsSigned);
    }

    [Fact]
    public async Task Approving_is_refused_without_the_confirmation_too()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;

        await Assert.ThrowsAsync<ConflictException>(() => new ApproveSigningCommandHandler(
            fixture.Db, fixture.GetHandler<IWorkflowEngine>(), fixture.GetHandler<IStorageService>(), fixture.GetHandler<IPdfStampService>(),
            fixture.GetHandler<IHtmlTemplatePdfGenerator>(), fixture.GetHandler<IAuditService>(), fixture.GetHandler<INotificationHub>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ApproveSigningCommandHandler>.Instance)
            .HandleAsync(new ApproveSigningCommand(seed.Signer.SecureToken, "127.0.0.1", "test")));
    }

    [Fact]
    public async Task The_confirmation_is_recorded_once_and_asking_again_changes_nothing()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;
        var command = new AcknowledgeReviewCommand(seed.Signer.SecureToken, "127.0.0.1", "test");

        await Acknowledge(fixture).HandleAsync(command);
        var first = (await fixture.Db.Recipients.FirstAsync(r => r.Id == seed.Signer.Id)).ReviewAcknowledgedAt;
        await Task.Delay(20);
        await Acknowledge(fixture).HandleAsync(command);

        Assert.NotNull(first);
        Assert.Equal(first, (await fixture.Db.Recipients.FirstAsync(r => r.Id == seed.Signer.Id)).ReviewAcknowledgedAt);
    }

    [Fact]
    public async Task A_viewer_has_no_task_and_is_not_asked()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;
        var viewer = Recipient.Create(seed.Document.Id, "Vera Viewer", "member@test.com", 3, ApprovalType.ViewOnly);
        fixture.Db.Recipients.Add(viewer);
        await fixture.Db.SaveChangesAsync();

        await Acknowledge(fixture).HandleAsync(new AcknowledgeReviewCommand(viewer.SecureToken, "127.0.0.1", "test"));

        Assert.Null((await fixture.Db.Recipients.FirstAsync(r => r.Id == viewer.Id)).ReviewAcknowledgedAt);
        Assert.False((await Session(fixture, viewer)).ReviewGate!.Required);
    }

    [Fact]
    public async Task A_member_of_the_organisation_is_shown_the_review_with_its_findings()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;

        var gate = (await Session(fixture, seed.Signer)).ReviewGate!;

        Assert.True(gate.Required);
        Assert.False(gate.Acknowledged);
        Assert.False(gate.ReviewRestricted);                          // matched by email, whatever the letter case
        var review = gate.Review!;
        Assert.Equal(ReadinessVerdict.NeedsWork, review.Verdict);
        Assert.Equal((89, 11), (review.Score, review.RiskScore));                     // 100 − 10 − 1; 10 + 1
        Assert.Equal((2, 0, 0), (review.OpenCount, review.CriticalCount, review.HandledCount));
        Assert.Equal(["Risque liability", "Risque notices"], review.Items.Select(i => i.Title));   // most severe first
        Assert.Equal("Article 2 — Responsabilité", review.Items[0].ClauseReference);
    }

    [Fact]
    public async Task An_outside_signer_must_read_the_document_but_is_not_shown_the_findings()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;

        var gate = (await Session(fixture, seed.Outsider)).ReviewGate!;

        Assert.True(gate.Required);
        Assert.True(gate.ReviewRestricted);
        Assert.Null(gate.Review);
    }

    [Fact]
    public async Task A_contract_nobody_reviewed_still_asks_for_the_confirmation_but_has_no_review_to_show()
    {
        var seed = await SeedAsync(reviewed: false);
        using var fixture = seed.Fixture;

        var gate = (await Session(fixture, seed.Signer)).ReviewGate!;

        Assert.True(gate.Required);
        Assert.False(gate.ReviewRestricted);
        Assert.Null(gate.Review);
    }

    [Fact]
    public async Task The_session_says_when_the_participant_has_already_confirmed()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;

        await Acknowledge(fixture).HandleAsync(new AcknowledgeReviewCommand(seed.Signer.SecureToken, "127.0.0.1", "test"));

        Assert.True((await Session(fixture, seed.Signer)).ReviewGate!.Acknowledged);
    }

    [Fact]
    public async Task A_new_version_of_the_contract_asks_everyone_to_confirm_again()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;
        await Acknowledge(fixture).HandleAsync(new AcknowledgeReviewCommand(seed.Signer.SecureToken, "127.0.0.1", "test"));

        (await fixture.Db.Recipients.FirstAsync(r => r.Id == seed.Signer.Id)).ResetForRevision();
        await fixture.Db.SaveChangesAsync();

        Assert.False((await Session(fixture, seed.Signer)).ReviewGate!.Acknowledged);
        await Assert.ThrowsAsync<ConflictException>(() => Sign(fixture, seed.Signer, seed.Field));
    }

    [Fact]
    public async Task Someone_who_already_responded_cannot_confirm()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;
        var recipient = await fixture.Db.Recipients.FirstAsync(r => r.Id == seed.Signer.Id);
        recipient.Reject("No");
        await fixture.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            Acknowledge(fixture).HandleAsync(new AcknowledgeReviewCommand(seed.Signer.SecureToken, "127.0.0.1", "test")));
    }
}
