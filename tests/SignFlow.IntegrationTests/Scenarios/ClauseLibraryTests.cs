using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Clauses;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// The clause library (M9): an approved wording never changes, a new one is drafted beside it; nobody approves their
/// own wording; a finding proposes the organisation's clause for its rule.
/// </summary>
public class ClauseLibraryTests
{
    private static readonly Guid AuthorId = Guid.NewGuid();

    private static async Task<(ScenarioFixture F, Guid Reviewer)> FixtureAsync()
    {
        var f = new ScenarioFixture(AuthorId, "author@test.com");
        var users = f.GetHandler<UserManager<ApplicationUser>>();
        foreach (var (id, email) in new[] { (AuthorId, "author@test.com"), (Guid.NewGuid(), "reviewer@test.com") })
        {
            Assert.True((await users.CreateAsync(new ApplicationUser
            {
                Id = id, FirstName = email[..1].ToUpper() + email[1..email.IndexOf('@')], LastName = "Test",
                UserName = email, Email = email, IsActive = true
            })).Succeeded);
        }
        var reviewer = await f.Db.Users.Where(u => u.Email == "reviewer@test.com").Select(u => u.Id).SingleAsync();
        return (f, reviewer);
    }

    private static FakeCurrentUserService Me(ScenarioFixture f) => (FakeCurrentUserService)f.GetHandler<ICurrentUserService>();

    private static void ActAs(ScenarioFixture f, Guid id, string email) { Me(f).UserIdOverride = id; Me(f).EmailOverride = email; }

    private static async Task<Guid> CreateAsync(ScenarioFixture f, string body = "<p>Chaque partie garde confidentielles les informations reçues.</p>", string? ruleKey = null) =>
        await new CreateClauseCommandHandler(f.Db, f.GetHandler<ICurrentUserService>(), NullLogger<CreateClauseCommandHandler>.Instance)
            .HandleAsync(new CreateClauseCommand("Confidentialité", ClauseCategory.Confidentiality, body, "fr", null, ClauseRiskLevel.Medium, null, ruleKey));

    private static Task ApproveAsync(ScenarioFixture f, Guid id, int number) =>
        new ApproveClauseVersionCommandHandler(f.Db, f.GetHandler<ICurrentUserService>(), NullLogger<ApproveClauseVersionCommandHandler>.Instance)
            .HandleAsync(new ApproveClauseVersionCommand(id, number));

    private static Task<int> RedraftAsync(ScenarioFixture f, Guid id, string body) =>
        new RedraftClauseCommandHandler(f.Db, f.GetHandler<ICurrentUserService>()).HandleAsync(new RedraftClauseCommand(id, body, "fr"));

    private static async Task<ClauseDto> ReadAsync(ScenarioFixture f, Guid id) =>
        (await new GetClausesQueryHandler(f.Db, f.GetHandler<ICurrentUserService>()).HandleAsync(new GetClausesQuery())).Single(c => c.Id == id);

    [Fact]
    public async Task Editing_an_approved_clause_leaves_the_approved_wording_unchanged_and_drafts_the_next_version()
    {
        var (f, reviewer) = await FixtureAsync();
        using var _ = f;
        var id = await CreateAsync(f);
        ActAs(f, reviewer, "reviewer@test.com");
        await ApproveAsync(f, id, 1);

        ActAs(f, AuthorId, "author@test.com");
        var number = await RedraftAsync(f, id, "<p>Pendant cinq ans après la fin du contrat.</p>");

        Assert.Equal(2, number);
        var clause = await ReadAsync(f, id);
        Assert.Equal(1, clause.Approved!.Number);
        Assert.Contains("informations reçues", clause.Approved.Body);
        Assert.Equal(ClauseStatus.Draft, clause.Latest!.Status);
        Assert.Equal(2, clause.Latest.Number);

        // What drafters are offered is still version 1 until version 2 is approved.
        var offered = await new GetApprovedClausesQueryHandler(f.Db).HandleAsync(new GetApprovedClausesQuery());
        Assert.Equal(1, offered.Single().Version);

        ActAs(f, reviewer, "reviewer@test.com");
        await ApproveAsync(f, id, 2);
        clause = await ReadAsync(f, id);
        Assert.Equal(2, clause.Approved!.Number);
        Assert.Equal(ClauseStatus.Deprecated, clause.Versions.Single(v => v.Number == 1).Status);
    }

    [Fact]
    public async Task The_author_of_a_wording_cannot_approve_it()
    {
        var (f, _) = await FixtureAsync();
        using var __ = f;
        var id = await CreateAsync(f);

        var refused = await Assert.ThrowsAsync<ForbiddenAccessException>(() => ApproveAsync(f, id, 1));

        Assert.Equal("clause.ownWording", refused.Code);
        Assert.Null((await ReadAsync(f, id)).Approved);
    }

    [Fact]
    public async Task A_draft_is_rewritten_in_place_and_its_last_writer_is_its_author()
    {
        var (f, reviewer) = await FixtureAsync();
        using var _ = f;
        var id = await CreateAsync(f);

        ActAs(f, reviewer, "reviewer@test.com");
        Assert.Equal(1, await RedraftAsync(f, id, "<p>Texte revu.</p>"));

        var clause = await ReadAsync(f, id);
        Assert.Single(clause.Versions);
        Assert.Equal(reviewer, clause.Latest!.AuthorId);
        // The reviewer wrote it last, so now they cannot approve it, and the original author can.
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => ApproveAsync(f, id, 1));
        ActAs(f, AuthorId, "author@test.com");
        await ApproveAsync(f, id, 1);
    }

    [Fact]
    public async Task Wording_is_sanitised_on_save()
    {
        var (f, _) = await FixtureAsync();
        using var __ = f;

        var id = await CreateAsync(f, "<p>Prix</p><img src=x onerror=alert(1)><script>alert(2)</script>");

        var body = (await ReadAsync(f, id)).Latest!.Body;
        Assert.Contains("<p>Prix</p>", body);
        Assert.DoesNotContain("onerror", body);
        Assert.DoesNotContain("alert", body);
    }

    [Fact]
    public async Task A_finding_on_a_linked_rule_finds_the_approved_library_clause_and_never_a_draft()
    {
        var (f, reviewer) = await FixtureAsync();
        using var _ = f;
        // "Add to library" from a finding: a draft linked to that rule (R-M9-4).
        var id = await CreateAsync(f, ruleKey: "confidentiality.duration");
        var forRule = new GetClausesForRuleQueryHandler(f.Db);

        Assert.Empty(await forRule.HandleAsync(new GetClausesForRuleQuery("confidentiality.duration", "fr")));

        ActAs(f, reviewer, "reviewer@test.com");
        await ApproveAsync(f, id, 1);
        var found = Assert.Single(await forRule.HandleAsync(new GetClausesForRuleQuery("confidentiality.duration", "fr")));
        Assert.Equal(id, found.Id);
        Assert.Equal(["confidentiality.duration"], found.RuleKeys);

        await new LinkClauseRulesCommandHandler(f.Db, f.GetHandler<ICurrentUserService>())
            .HandleAsync(new LinkClauseRulesCommand(id, ["liability.cap"]));
        Assert.Empty(await forRule.HandleAsync(new GetClausesForRuleQuery("confidentiality.duration")));
        Assert.Single(await forRule.HandleAsync(new GetClausesForRuleQuery("liability.cap")));
    }

    [Fact]
    public async Task A_library_clause_in_a_template_keeps_its_approved_words_and_the_contract_records_its_version()
    {
        var (f, reviewer) = await FixtureAsync();
        using var _ = f;
        var id = await CreateAsync(f, "<p>Chaque partie garde confidentielles les informations reçues.</p>");
        ActAs(f, reviewer, "reviewer@test.com");
        await ApproveAsync(f, id, 1);

        var block = $"<div class=\"library-clause\" data-clause-id=\"{id}\" data-clause-version=\"1\" data-locked=\"true\">" +
                    "<p>Chaque partie garde confidentielles\n   les informations reçues.</p></div>";
        await SignFlow.Application.Common.Services.LockedClauses.CheckAsync(f.Db, "<h2>Article 5</h2>" + block, default);

        var used = await SignFlow.Application.Common.Services.LockedClauses.UsedAsync(f.Db, block + block, default);
        var only = Assert.Single(used);
        Assert.Equal(("Confidentialité", 1), (only.Title, only.Version));

        var edited = block.Replace("confidentielles", "secrètes");
        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            SignFlow.Application.Common.Services.LockedClauses.CheckAsync(f.Db, edited, default));
        Assert.Equal("clause.lockedEdited", refused.Code);
    }
}
