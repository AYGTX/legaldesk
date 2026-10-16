using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.RegulatoryWatch.Commands;
using SignFlow.Application.Features.RegulatoryWatch.Queries;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// The rules that make the stored law trustworthy: a text is only "verified" when a named person signed it off against
/// a named source, and the sign-off cannot outlive a change to what was checked. The law texts in these tests are
/// invented placeholders, not quotations of any statute.
/// </summary>
public class LegalCorpusTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed record Seed(ScenarioFixture Fixture, RegulatoryText Text, FakeCurrentUserService User);

    private static async Task<Seed> SeedAsync(string? reference = "Loi n° 00-00 du 1er janvier 2000", string? sourceUrl = "https://example.org/jort/2000/001")
    {
        var fixture = new ScenarioFixture(UserId);
        var db = (ApplicationDbContext)fixture.Db;

        var text = RegulatoryText.Create("Loi d'essai", RegulatoryCategory.DataProtection, RegulatoryJurisdiction.Tunisia,
            "Résumé.", "essai", UserId, "fr", reference, sourceUrl: sourceUrl);
        db.RegulatoryTexts.Add(text);
        await db.SaveChangesAsync();

        return new Seed(fixture, text, (FakeCurrentUserService)fixture.GetHandler<ICurrentUserService>());
    }

    private static SaveRegulatoryArticlesCommandHandler Save(ScenarioFixture f) =>
        new(f.Db, f.GetHandler<ICurrentUserService>(), NullLogger<SaveRegulatoryArticlesCommandHandler>.Instance);

    private static VerifyRegulatoryTextCommandHandler Verify(ScenarioFixture f) =>
        new(f.Db, f.GetHandler<ICurrentUserService>(), NullLogger<VerifyRegulatoryTextCommandHandler>.Instance);

    private static List<RegulatoryArticleInput> Articles(params (string Number, string Text)[] a) =>
        a.Select(x => new RegulatoryArticleInput(x.Number, x.Text)).ToList();

    [Fact]
    public async Task Saved_articles_are_stored_in_the_order_given()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;

        await Save(fixture).HandleAsync(new SaveRegulatoryArticlesCommand(seed.Text.Id,
            Articles(("1", "Premier."), ("2", "Deuxième."), ("2 bis", "Inséré."), ("3", "Troisième."))));

        var stored = await new GetRegulatoryArticlesQueryHandler(fixture.Db, fixture.GetHandler<ICurrentUserService>())
            .HandleAsync(new GetRegulatoryArticlesQuery(seed.Text.Id));
        Assert.Equal(["1", "2", "2 bis", "3"], stored.Select(a => a.Number));
        Assert.Equal([1, 2, 3, 4], stored.Select(a => a.Order));
    }

    [Fact]
    public async Task Saving_again_replaces_the_articles_it_does_not_add_to_them()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;

        await Save(fixture).HandleAsync(new SaveRegulatoryArticlesCommand(seed.Text.Id, Articles(("1", "A."), ("2", "B."))));
        await Save(fixture).HandleAsync(new SaveRegulatoryArticlesCommand(seed.Text.Id, Articles(("1", "A corrigé."))));

        var stored = await fixture.Db.RegulatoryArticles.ToListAsync();
        Assert.Equal("A corrigé.", Assert.Single(stored).Text);
    }

    [Fact]
    public async Task A_text_can_be_verified_once_it_has_articles_a_reference_and_a_source()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;
        await Save(fixture).HandleAsync(new SaveRegulatoryArticlesCommand(seed.Text.Id, Articles(("1", "A."))));

        await Verify(fixture).HandleAsync(new VerifyRegulatoryTextCommand(seed.Text.Id));

        var text = await fixture.Db.RegulatoryTexts.SingleAsync();
        Assert.True(text.IsVerified);
        Assert.Equal(UserId, text.VerifiedById);
        Assert.NotNull(text.VerifiedAt);
    }

    [Fact]
    public async Task Nothing_is_verified_without_articles()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Verify(fixture).HandleAsync(new VerifyRegulatoryTextCommand(seed.Text.Id)));

        Assert.Contains("no articles", ex.Message);
    }

    [Theory]
    [InlineData(null, "https://example.org/x", "reference")]
    [InlineData("Loi n° 1", null, "source")]
    [InlineData("  ", "https://example.org/x", "reference")]
    public async Task A_sign_off_must_name_what_it_was_checked_against(string? reference, string? source, string missing)
    {
        var seed = await SeedAsync(reference, source);
        using var fixture = seed.Fixture;
        await Save(fixture).HandleAsync(new SaveRegulatoryArticlesCommand(seed.Text.Id, Articles(("1", "A."))));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Verify(fixture).HandleAsync(new VerifyRegulatoryTextCommand(seed.Text.Id)));

        Assert.Contains(missing, ex.Message);
        Assert.False((await fixture.Db.RegulatoryTexts.SingleAsync()).IsVerified);
    }

    [Fact]
    public async Task Changing_the_articles_withdraws_the_verification()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;
        await Save(fixture).HandleAsync(new SaveRegulatoryArticlesCommand(seed.Text.Id, Articles(("1", "A."))));
        await Verify(fixture).HandleAsync(new VerifyRegulatoryTextCommand(seed.Text.Id));

        await Save(fixture).HandleAsync(new SaveRegulatoryArticlesCommand(seed.Text.Id, Articles(("1", "A modifié."))));

        var text = await fixture.Db.RegulatoryTexts.SingleAsync();
        Assert.False(text.IsVerified);
        Assert.Null(text.VerifiedById);
    }

    [Fact]
    public async Task Changing_the_reference_or_the_source_withdraws_the_verification_but_other_edits_do_not()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;
        await Save(fixture).HandleAsync(new SaveRegulatoryArticlesCommand(seed.Text.Id, Articles(("1", "A."))));
        await Verify(fixture).HandleAsync(new VerifyRegulatoryTextCommand(seed.Text.Id));

        var update = new UpdateRegulatoryTextCommandHandler(fixture.Db, fixture.GetHandler<ICurrentUserService>(),
            NullLogger<UpdateRegulatoryTextCommandHandler>.Instance);

        // A new title or summary does not touch what the verifier checked.
        await update.HandleAsync(new UpdateRegulatoryTextCommand(seed.Text.Id, "Nouveau titre", RegulatoryCategory.DataProtection,
            RegulatoryJurisdiction.Tunisia, "Nouveau résumé.", "essai", "fr", seed.Text.Reference, null, null, seed.Text.SourceUrl));
        Assert.True((await fixture.Db.RegulatoryTexts.SingleAsync()).IsVerified);

        // A different official reference means the sign-off no longer describes this text.
        await update.HandleAsync(new UpdateRegulatoryTextCommand(seed.Text.Id, "Nouveau titre", RegulatoryCategory.DataProtection,
            RegulatoryJurisdiction.Tunisia, "Nouveau résumé.", "essai", "fr", "Loi n° 99", null, null, seed.Text.SourceUrl));
        Assert.False((await fixture.Db.RegulatoryTexts.SingleAsync()).IsVerified);
    }

    [Fact]
    public async Task Only_administrators_and_managers_curate_the_law()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;
        seed.User.RolesOverride = ["Employee"];

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Save(fixture).HandleAsync(new SaveRegulatoryArticlesCommand(seed.Text.Id, Articles(("1", "A.")))));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Verify(fixture).HandleAsync(new VerifyRegulatoryTextCommand(seed.Text.Id)));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            new PreviewRegulatoryArticlesQueryHandler(fixture.GetHandler<ICurrentUserService>())
                .HandleAsync(new PreviewRegulatoryArticlesQuery("Article 1 - A.")));
    }

    [Fact]
    public async Task The_listing_reports_the_article_count_and_who_verified()
    {
        var seed = await SeedAsync();
        using var fixture = seed.Fixture;
        fixture.Db.Users.Add(new ApplicationUser { Id = UserId, Email = "legal@test.com", UserName = "legal@test.com", FirstName = "Lea", LastName = "Legal", IsActive = true, SecurityStamp = Guid.NewGuid().ToString() });
        await fixture.Db.SaveChangesAsync();
        await Save(fixture).HandleAsync(new SaveRegulatoryArticlesCommand(seed.Text.Id, Articles(("1", "A."), ("2", "B."))));
        await Verify(fixture).HandleAsync(new VerifyRegulatoryTextCommand(seed.Text.Id));

        var listed = Assert.Single(await new GetRegulatoryTextsQueryHandler(fixture.Db, fixture.GetHandler<ICurrentUserService>())
            .HandleAsync(new GetRegulatoryTextsQuery()));

        Assert.Equal(2, listed.ArticleCount);
        Assert.NotNull(listed.VerifiedAt);
        Assert.Equal("Lea Legal", listed.VerifiedByName);
        Assert.Equal("https://example.org/jort/2000/001", listed.SourceUrl);
    }

    [Fact]
    public async Task Two_articles_cannot_share_a_number()
    {
        var validator = new SaveRegulatoryArticlesCommandValidator();

        var result = await validator.ValidateAsync(new SaveRegulatoryArticlesCommand(Guid.NewGuid(), Articles(("1", "A."), ("1", "B."))));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("same number"));
    }
}
