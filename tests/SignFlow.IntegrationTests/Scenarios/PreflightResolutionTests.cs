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
/// The pre-flight review of rework spec M8, end to end with the real handlers and a fake Harvey: findings only from
/// Harvey, one item per (rule, clause), decisions with reasons and the right role, the send gate, revisions, and a
/// Harvey that is down or slow.
/// </summary>
public class PreflightResolutionTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private const string Liability = "Article 2 — Responsabilité";
    private const string Term = "Article 3 — Durée";

    private static async Task<(ScenarioFixture Fixture, Document Document)> DraftAsync(params HarveyFinding[] findings)
    {
        var fixture = new ScenarioFixture(OwnerId);
        var db = (ApplicationDbContext)fixture.Db;
        db.Set<ApplicationUser>().Add(new ApplicationUser
        {
            Id = OwnerId, Email = "owner@test.com", UserName = "owner@test.com", FirstName = "Test", LastName = "Owner",
            IsActive = true, SecurityStamp = Guid.NewGuid().ToString()
        });
        var document = Document.Create("Supply agreement", "supply.pdf", "documents/supply.pdf", 10, 1, OwnerId);
        db.Documents.Add(document);
        db.Recipients.Add(Recipient.Create(document.Id, "Signer", "signer@partner.com", 1, ApprovalType.Signature));
        await db.SaveChangesAsync();
        fixture.Harvey.Findings = [.. findings];
        return (fixture, document);
    }

    private static FakeCurrentUserService User(ScenarioFixture f) => (FakeCurrentUserService)f.GetHandler<ICurrentUserService>();

    private static Task<ReviewDto> Decide(ScenarioFixture f, Document d, Guid itemId, ReadinessItemStatus status, string? reason) =>
        f.GetHandler<DecideReviewItemCommandHandler>().HandleAsync(new DecideReviewItemCommand(d.Id, itemId, status, reason));

    private static Task Send(ScenarioFixture f, Document d, string? reason = null) =>
        f.GetHandler<SendDocumentCommandHandler>().HandleAsync(new SendDocumentCommand(d.Id, reason));

    private static async Task<int> ReviseAsync(ScenarioFixture f, Document d)
    {
        var handler = new UploadRevisionCommandHandler(
            f.Db, f.GetHandler<IStorageService>(), f.GetHandler<IPdfService>(), f.GetHandler<PdfUploadGuard>(),
            f.GetHandler<IReviewQueue>(), f.GetHandler<ICurrentUserService>(), f.GetHandler<IAuditService>(), NullLogger<UploadRevisionCommandHandler>.Instance);
        var result = await handler.HandleAsync(new UploadRevisionCommand(
            d.Id, new MemoryStream(NullPdfInspector.FakePdf), "supply-v2.pdf", NullPdfInspector.FakePdf.Length, "application/pdf", "Revised"));
        return result.Version;
    }

    // ------------------------------------------------------------------ findings and grading

    [Fact]
    public async Task One_item_per_rule_and_clause_keeps_every_quote_and_its_origin()
    {
        var (fixture, document) = await DraftAsync(
            FakeHarvey.Finding("liability", "High", Liability, "n'est responsable d'aucun dommage"),
            FakeHarvey.Finding("liability", "High", Liability, "quelle qu'en soit la cause", "MeaningMatch"),
            FakeHarvey.Finding("liability", "High", Term, "durée d'un an"));
        using var _ = fixture;
        fixture.Harvey.Approvals["liability"] = new RuleApproval(1, "Me Trabelsi (Lawyer)", new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc));
        fixture.Harvey.Articles["liability"] = [new PlaybookArticle("a1", "Code des obligations et des contrats (Décret 1906), art. 244", "Toute clause exonératoire de la faute lourde est nulle.", "Faute lourde")];

        var review = await fixture.ReviewAsync(document.Id);

        Assert.Equal(2, review.Items.Count);
        var first = review.Items.Single(i => i.ClauseReference == Liability);
        Assert.Equal(["n'est responsable d'aucun dommage", "quelle qu'en soit la cause"], first.Quotes);
        Assert.Equal(("CodeCheck", 1, "Me Trabelsi (Lawyer)"), (first.DetectionKind, first.RuleVersion, first.RuleApprovedBy));
        Assert.Equal("Code des obligations et des contrats, art. 244", Assert.Single(first.Articles).Citation);
        Assert.Equal((ReadinessVerdict.NeedsWork, 80, 20), (review.Verdict, review.ReadinessScore, review.RiskScore));
        Assert.Equal("grid-1", review.LatestRun!.GridVersion);
    }

    [Fact]
    public async Task A_contract_with_no_finding_is_ready_and_one_with_an_open_critical_is_blocked()
    {
        var (clean, cleanDoc) = await DraftAsync();
        using (clean)
            Assert.Equal((ReadinessVerdict.Ready, 100, 0), await Grade(clean, cleanDoc));

        var (risky, riskyDoc) = await DraftAsync(FakeHarvey.Finding("illicit", "Critical", Liability), FakeHarvey.Finding("notice", "Medium", Term));
        using (risky)
            Assert.Equal((ReadinessVerdict.Blocked, 72, 28), await Grade(risky, riskyDoc));

        static async Task<(ReadinessVerdict, int?, int?)> Grade(ScenarioFixture f, Document d)
        {
            var r = await f.ReviewAsync(d.Id);
            return (r.Verdict, r.ReadinessScore, r.RiskScore);
        }
    }

    [Fact]
    public async Task The_organisation_settings_travel_with_the_review()
    {
        var (fixture, document) = await DraftAsync(FakeHarvey.Finding("liability", "High", Liability), FakeHarvey.Finding("notice", "Low", Term));
        using var _ = fixture;
        var settings = fixture.GetHandler<SetReviewSettingCommandHandler>();
        await settings.HandleAsync(new SetReviewSettingCommand("notice", Enabled: false, SeverityOverride: null));
        await settings.HandleAsync(new SetReviewSettingCommand("liability", Enabled: true, SeverityOverride: "Critical"));

        var review = await fixture.ReviewAsync(document.Id);

        Assert.Equal(["notice"], fixture.Harvey.DisabledSent.Last());
        var item = Assert.Single(review.Items);
        Assert.Equal(("liability", "Critical"), (item.RuleKey, item.Severity));
        Assert.Equal(ReadinessVerdict.Blocked, review.Verdict);
    }

    // ------------------------------------------------------------------ decisions

    [Fact]
    public async Task Accepting_a_critical_needs_a_manager_and_a_reason()
    {
        var (fixture, document) = await DraftAsync(FakeHarvey.Finding("illicit", "Critical", Liability));
        using var _ = fixture;
        var item = Assert.Single((await fixture.ReviewAsync(document.Id)).Items);

        User(fixture).RolesOverride = ["Employee"];
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Decide(fixture, document, item.Id, ReadinessItemStatus.Accepted, "Fine"));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Decide(fixture, document, item.Id, ReadinessItemStatus.NotApplicable, "Fine"));

        User(fixture).RolesOverride = ["Manager"];
        await Assert.ThrowsAsync<ValidationException>(() => Decide(fixture, document, item.Id, ReadinessItemStatus.Accepted, "  "));

        var review = await Decide(fixture, document, item.Id, ReadinessItemStatus.Accepted, "Capped by the framework agreement.");

        var decided = Assert.Single(review.Items);
        Assert.Equal((ReadinessItemStatus.Accepted, "Capped by the framework agreement.", "Test Owner"), (decided.Status, decided.Reason, decided.DecidedBy));
        Assert.Equal((ReadinessVerdict.Ready, 100, 25), (review.Verdict, review.ReadinessScore, review.RiskScore)); // risk counts handled findings
    }

    [Fact]
    public async Task The_owner_accepts_a_high_with_a_reason_and_nobody_marks_a_finding_fixed()
    {
        var (fixture, document) = await DraftAsync(FakeHarvey.Finding("liability", "High", Liability));
        using var _ = fixture;
        User(fixture).RolesOverride = ["Employee"];
        var item = Assert.Single((await fixture.ReviewAsync(document.Id)).Items);

        await Assert.ThrowsAsync<ValidationException>(() => Decide(fixture, document, item.Id, ReadinessItemStatus.Accepted, null));
        await Assert.ThrowsAsync<ValidationException>(() => Decide(fixture, document, item.Id, ReadinessItemStatus.Fixed, "done"));
        var review = await Decide(fixture, document, item.Id, ReadinessItemStatus.Accepted, "Accepted in negotiation");
        Assert.Equal(ReadinessVerdict.Ready, review.Verdict);

        // Back to open: the verdict follows.
        review = await Decide(fixture, document, item.Id, ReadinessItemStatus.Open, null);
        Assert.Equal((ReadinessVerdict.NeedsWork, ReadinessItemStatus.Open), (review.Verdict, review.Items[0].Status));
    }

    [Fact]
    public async Task Not_applicable_is_reported_to_harvey_with_the_reason()
    {
        var (fixture, document) = await DraftAsync(FakeHarvey.Finding("liability", "High", Liability, "aucun dommage"));
        using var _ = fixture;
        var item = Assert.Single((await fixture.ReviewAsync(document.Id)).Items);

        await Decide(fixture, document, item.Id, ReadinessItemStatus.NotApplicable, "Clause d'un contrat cadre, pas celle-ci.");

        var queued = Assert.Single(fixture.ReviewQueue.Feedback);
        await fixture.GetHandler<ReviewFeedbackSender>().SendAsync(queued);
        var report = Assert.Single(fixture.Harvey.Feedback);
        Assert.Equal((item.Id.ToString(), "liability", 1, Liability, "aucun dommage", "Clause d'un contrat cadre, pas celle-ci."),
            (report.SourceRef, report.RuleKey, report.RuleVersion, report.ClauseReference, report.Quote, report.Reason));
    }

    [Fact]
    public async Task A_sent_contract_review_cannot_change()
    {
        var (fixture, document) = await DraftAsync(FakeHarvey.Finding("notice", "Low", Term));
        using var _ = fixture;
        var item = Assert.Single((await fixture.ReviewAsync(document.Id)).Items);
        await Send(fixture, document);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => Decide(fixture, document, item.Id, ReadinessItemStatus.Accepted, "late"));
        Assert.Equal("review.locked", refused.Code);
    }

    // ------------------------------------------------------------------ re-runs and revisions

    [Fact]
    public async Task Re_running_the_same_version_leaves_every_state_unchanged()
    {
        var (fixture, document) = await DraftAsync(FakeHarvey.Finding("liability", "High", Liability), FakeHarvey.Finding("notice", "Low", Term));
        using var _ = fixture;
        var first = await fixture.ReviewAsync(document.Id);
        await Decide(fixture, document, first.Items.Single(i => i.RuleKey == "notice").Id, ReadinessItemStatus.NotApplicable, "Pas pertinent");

        // Same text; Harvey now finds only one of the two (a rule withdrawn, say).
        fixture.Harvey.Findings = [FakeHarvey.Finding("liability", "High", Liability)];
        var second = await fixture.ReviewAsync(document.Id);

        var states = await fixture.Db.DocumentReadinessItems.ToDictionaryAsync(i => i.RuleKey!, i => i.Status);
        Assert.Equal(ReadinessItemStatus.Open, states["liability"]);
        Assert.Equal(ReadinessItemStatus.NotApplicable, states["notice"]);       // not Fixed: the text did not change
        Assert.Equal(["liability"], second.Items.Select(i => i.RuleKey));        // the current findings are this run's
        Assert.Empty(second.FixedItems);
    }

    [Fact]
    public async Task A_revision_needs_its_own_review_and_what_it_no_longer_finds_is_fixed_in_that_version()
    {
        var (fixture, document) = await DraftAsync(FakeHarvey.Finding("liability", "High", Liability), FakeHarvey.Finding("notice", "Low", Term));
        using var _ = fixture;
        await fixture.ReviewAsync(document.Id);

        Assert.Equal(2, await ReviseAsync(fixture, document));
        var refused = await Assert.ThrowsAsync<ValidationException>(() => Send(fixture, document, "urgent"));
        Assert.Contains("readinessRequired", refused.Errors.Keys);

        fixture.Harvey.Findings = [FakeHarvey.Finding("notice", "Low", Term)];
        var review = await fixture.ReviewAsync(document.Id);

        var fixedItem = Assert.Single(review.FixedItems);
        Assert.Equal(("liability", 2), (fixedItem.RuleKey, fixedItem.FixedInVersion));
        Assert.Equal((ReadinessVerdict.Ready, 99), (review.Verdict, review.ReadinessScore));
        await Send(fixture, document);

        // A risk fixed in one version that comes back in a later one is open again.
        var item = await fixture.Db.DocumentReadinessItems.SingleAsync(i => i.RuleKey == "liability");
        item.Seen(new ReviewFinding("liability", Liability, "High", "Risque", "x", null, null, null, 1, null, null, null, null, null), Guid.NewGuid(), 3);
        Assert.Equal((ReadinessItemStatus.Open, (int?)null), (item.Status, item.FixedInVersion));
    }

    // ------------------------------------------------------------------ the send gate (R-M8-8)

    [Fact]
    public async Task The_send_gate_refuses_unreviewed_and_blocked_and_asks_a_reason_for_needs_work()
    {
        var (fixture, document) = await DraftAsync(FakeHarvey.Finding("illicit", "Critical", Liability), FakeHarvey.Finding("liability", "High", Term));
        using var _ = fixture;

        Assert.Contains("readinessRequired", (await Assert.ThrowsAsync<ValidationException>(() => Send(fixture, document))).Errors.Keys);

        var review = await fixture.ReviewAsync(document.Id);
        Assert.Contains("readiness", (await Assert.ThrowsAsync<ValidationException>(() => Send(fixture, document, "please"))).Errors.Keys);

        User(fixture).RolesOverride = ["Manager"];
        await Decide(fixture, document, review.Items.Single(i => i.Severity == "Critical").Id, ReadinessItemStatus.Accepted, "Validated by legal");
        Assert.Contains("readinessWarning", (await Assert.ThrowsAsync<ValidationException>(() => Send(fixture, document))).Errors.Keys);

        await Send(fixture, document, "Client deadline; liability reviewed by phone");

        Assert.Equal(DocumentStatus.InProgress, (await fixture.Db.Documents.SingleAsync()).Status);
        var audit = (NullAuditService)fixture.GetHandler<IAuditService>();
        Assert.Contains(audit.Entries, e => e.Action == AuditAction.SentWithOpenRisks && e.Description!.Contains("Client deadline"));
    }

    // ------------------------------------------------------------------ Harvey down or slow (R-M8-9)

    [Fact]
    public async Task Harvey_down_refuses_the_review_with_a_code_and_changes_nothing()
    {
        var (fixture, document) = await DraftAsync(FakeHarvey.Finding("notice", "Low", Term));
        using var _ = fixture;
        await fixture.ReviewAsync(document.Id);
        var before = await fixture.Db.DocumentReadinessItems.AsNoTracking().SingleAsync();

        fixture.Harvey.Down = true;
        var refused = await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            fixture.GetHandler<StartReviewCommandHandler>().HandleAsync(new StartReviewCommand(document.Id)));

        Assert.Equal(ReviewRun.Unavailable, refused.Code);
        var after = await fixture.Db.DocumentReadinessItems.AsNoTracking().SingleAsync();
        Assert.Equal((before.Status, before.LastSeenRunId), (after.Status, after.LastSeenRunId));
        Assert.Equal(1, await fixture.Db.ReviewRuns.CountAsync());
    }

    [Fact]
    public async Task Harvey_failing_during_the_run_fails_it_visibly_and_the_verdict_stays_not_evaluated()
    {
        var (fixture, document) = await DraftAsync(FakeHarvey.Finding("notice", "Low", Term));
        using var _ = fixture;
        var run = await fixture.GetHandler<StartReviewCommandHandler>().HandleAsync(new StartReviewCommand(document.Id));

        fixture.Harvey.Down = true;   // up when asked, down when the job runs
        await fixture.GetHandler<ReviewRunner>().RunAsync(run.Id);

        var review = await fixture.GetHandler<GetLatestReviewQueryHandler>().HandleAsync(new GetLatestReviewQuery(document.Id));
        Assert.Equal((ReviewRunStatus.Failed, ReviewRun.Unavailable), (review.LatestRun!.Status, review.LatestRun.ErrorCode));
        Assert.Equal(ReadinessVerdict.NotEvaluated, review.Verdict);
        Assert.Empty(await fixture.Db.DocumentReadinessItems.ToListAsync());
    }

    [Fact]
    public async Task A_review_that_takes_too_long_times_out()
    {
        var (fixture, document) = await DraftAsync(FakeHarvey.Finding("notice", "Low", Term));
        using var _ = fixture;
        var run = await fixture.GetHandler<StartReviewCommandHandler>().HandleAsync(new StartReviewCommand(document.Id));
        fixture.Harvey.Delay = TimeSpan.FromSeconds(5);

        var runner = new ReviewRunner(fixture.Db, fixture.GetHandler<IContractTextProvider>(), fixture.GetHandler<IContractReviewService>(),
            fixture.GetHandler<INotificationHub>(), fixture.GetHandler<IAuditService>(),
            Options.Create(new ReviewOptions { TimeoutSeconds = 1 }), NullLogger<ReviewRunner>.Instance);
        await runner.RunAsync(run.Id);

        Assert.Equal(ReviewRun.TimedOut, (await fixture.Db.ReviewRuns.SingleAsync()).ErrorCode);
    }

    [Fact]
    public async Task A_grid_with_no_rule_in_service_fails_the_review_instead_of_calling_the_contract_ready()
    {
        var (fixture, document) = await DraftAsync();
        using var _ = fixture;
        fixture.Harvey.RulesInService.Clear();

        var review = await fixture.ReviewAsync(document.Id);

        Assert.Equal((ReviewRunStatus.Failed, ReviewRun.NoRules), (review.LatestRun!.Status, review.LatestRun.ErrorCode));
        Assert.Equal(ReadinessVerdict.NotEvaluated, review.Verdict);

        // Every rule switched off by the organisation is the same: nothing was checked.
        fixture.Harvey.RulesInService.Add("liability");
        await fixture.GetHandler<SetReviewSettingCommandHandler>().HandleAsync(new SetReviewSettingCommand("liability", false, null));
        Assert.Equal(ReviewRun.NoRules, (await fixture.ReviewAsync(document.Id)).LatestRun!.ErrorCode);
    }

    [Fact]
    public async Task Asking_twice_while_a_review_runs_returns_the_same_run()
    {
        var (fixture, document) = await DraftAsync();
        using var _ = fixture;
        var start = fixture.GetHandler<StartReviewCommandHandler>();

        var first = await start.HandleAsync(new StartReviewCommand(document.Id));
        var second = await start.HandleAsync(new StartReviewCommand(document.Id));

        Assert.Equal(first.Id, second.Id);
        Assert.Single(fixture.ReviewQueue.Reviews);
    }
}
