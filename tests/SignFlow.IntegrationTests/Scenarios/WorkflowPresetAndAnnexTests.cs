using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.HtmlTemplates.Commands;
using SignFlow.Application.Features.PdfTemplates.Commands;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;
using SignFlow.Infrastructure.Services.Pdf;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// A template that follows a workflow gets its approvers from it when a request is created: named people, a role or
/// department looked up on the spot, and steps dropped when their condition does not hold. A PDF template can also carry
/// library clauses as an annex, appended after the file.
/// </summary>
public class WorkflowPresetRoutingTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private static async Task<ApplicationUser> AddUserAsync(
        ScenarioFixture f, string first, string? department = null, string? role = null, bool active = true, Guid? id = null)
    {
        var users = f.GetHandler<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = id ?? Guid.NewGuid(), FirstName = first, LastName = "Test", UserName = $"{first.ToLower()}@test.com",
            Email = $"{first.ToLower()}@test.com", Department = department, IsActive = active, EmailConfirmed = true
        };
        Assert.True((await users.CreateAsync(user)).Succeeded);

        if (role is not null)
        {
            var roles = f.GetHandler<RoleManager<IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole<Guid>(role));
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }
        return user;
    }

    private static WorkflowPresetStep UserStep(Guid presetId, int order, string label, Guid userId, int stage = 1,
        string? conditionKey = null, string? op = null, string? value = null)
        => WorkflowPresetStep.Create(presetId, order, stage, label, PresetApproverKind.User, userId, null, null, conditionKey, op, value);

    private static async Task<WorkflowPreset> AddPresetAsync(
        ScenarioFixture f, WorkflowType type, Func<Guid, WorkflowPresetStep[]> steps, bool active = true)
    {
        var preset = WorkflowPreset.Create("Circuit", null, type);
        if (!active) preset.Update("Circuit", null, type, false);
        f.Db.WorkflowPresets.Add(preset);
        f.Db.WorkflowPresetSteps.AddRange(steps(preset.Id));
        await f.Db.SaveChangesAsync();
        return preset;
    }

    /// <summary>A template asking for an amount, with <paramref name="signatures"/> approver signature fields.</summary>
    private static async Task<HtmlTemplate> AddTemplateAsync(
        ScenarioFixture f, Guid? presetId, int signatures, WorkflowType type = WorkflowType.Sequential, int[]? stages = null)
    {
        var template = HtmlTemplate.Create("Demande", "<p>{{amount}}</p>", OwnerId, workflowType: type);
        template.SetWorkflowPreset(presetId);
        f.Db.HtmlTemplates.Add(template);
        f.Db.HtmlTemplateFields.Add(HtmlTemplateField.Create(template.Id, "amount", "Montant", HtmlTemplateFieldType.Number,
            HtmlTemplateFieldRole.Initiator, true, 1));
        for (var i = 0; i < signatures; i++)
            f.Db.HtmlTemplateFields.Add(HtmlTemplateField.Create(template.Id, $"sig{i + 1}", $"Signature {i + 1}", HtmlTemplateFieldType.Signature,
                HtmlTemplateFieldRole.Approver, true, i + 2, workflowStage: stages?[i] ?? 1));
        await f.Db.SaveChangesAsync();
        return template;
    }

    private static CreateRequestFromHtmlTemplateCommandHandler Handler(ScenarioFixture f) => new(
        f.Db, f.GetHandler<IWorkflowEngine>(), f.GetHandler<IWorkflowPresetResolver>(), f.GetHandler<ICurrentUserService>(),
        f.GetHandler<IAuditService>(), NullLogger<CreateRequestFromHtmlTemplateCommandHandler>.Instance);

    private static Task<Guid> SubmitAsync(ScenarioFixture f, HtmlTemplate t, string amount, params ApproverSelection[] approvers)
        => Handler(f).HandleAsync(new CreateRequestFromHtmlTemplateCommand(
            t.Id, new Dictionary<string, string> { ["amount"] = amount }, approvers.Length == 0 ? null : [.. approvers], "Demande test"));

    private static Task<List<Recipient>> RecipientsAsync(ScenarioFixture f, Guid documentId)
        => f.Db.Recipients.Where(r => r.DocumentId == documentId).OrderBy(r => r.Order).ToListAsync();

    private static async Task<ScenarioFixture> FixtureWithOwnerAsync()
    {
        var f = new ScenarioFixture(OwnerId);
        await AddUserAsync(f, "Owner", id: OwnerId);
        return f;
    }

    [Fact]
    public async Task Each_step_goes_to_its_person_in_order_and_keeps_its_label_as_the_role()
    {
        using var f = await FixtureWithOwnerAsync();
        var legal = await AddUserAsync(f, "Sami");
        var director = await AddUserAsync(f, "Nour");
        var preset = await AddPresetAsync(f, WorkflowType.Sequential,
            id => [UserStep(id, 1, "Revue juridique", legal.Id), UserStep(id, 2, "Direction", director.Id)]);
        var template = await AddTemplateAsync(f, preset.Id, signatures: 2);

        var recipients = await RecipientsAsync(f, await SubmitAsync(f, template, "500"));

        Assert.Equal(["sami@test.com", "nour@test.com"], recipients.Select(r => r.Email));
        Assert.Equal([1, 2], recipients.Select(r => r.Order));
        Assert.Equal(["Revue juridique", "Direction"], recipients.Select(r => r.RoleLabel));
    }

    [Theory]
    [InlineData("500", 1)]
    [InlineData("10000", 1)]
    [InlineData("50000", 2)]
    public async Task A_conditional_step_is_skipped_with_its_signature_field_when_the_condition_fails(string amount, int expectedSigners)
    {
        using var f = await FixtureWithOwnerAsync();
        var legal = await AddUserAsync(f, "Sami");
        var director = await AddUserAsync(f, "Nour");
        var preset = await AddPresetAsync(f, WorkflowType.Sequential, id =>
            [UserStep(id, 1, "Juridique", legal.Id), UserStep(id, 2, "Direction", director.Id, conditionKey: "amount", op: ">", value: "10000")]);
        var template = await AddTemplateAsync(f, preset.Id, signatures: 2);

        var documentId = await SubmitAsync(f, template, amount);

        Assert.Equal(expectedSigners, (await RecipientsAsync(f, documentId)).Count);
        Assert.Equal(expectedSigners, await f.Db.SignatureFields.CountAsync(s => s.DocumentId == documentId));
    }

    [Fact]
    public async Task A_department_step_resolves_to_the_one_active_person_in_it()
    {
        using var f = await FixtureWithOwnerAsync();
        await AddUserAsync(f, "Finance1", department: "Finance", active: false);
        var cfo = await AddUserAsync(f, "Cfo", department: "Finance");
        var preset = await AddPresetAsync(f, WorkflowType.Sequential, id =>
            [WorkflowPresetStep.Create(id, 1, 1, "Finance", PresetApproverKind.Department, null, null, "finance", null, null, null)]);
        var template = await AddTemplateAsync(f, preset.Id, signatures: 1);

        var recipients = await RecipientsAsync(f, await SubmitAsync(f, template, "1"));

        Assert.Equal(cfo.Email, Assert.Single(recipients).Email);
    }

    [Fact]
    public async Task A_role_step_resolves_to_the_one_active_person_holding_it()
    {
        using var f = await FixtureWithOwnerAsync();
        var dpo = await AddUserAsync(f, "Dpo", role: "DataProtection");
        var preset = await AddPresetAsync(f, WorkflowType.Sequential, id =>
            [WorkflowPresetStep.Create(id, 1, 1, null, PresetApproverKind.Role, null, "DataProtection", null, null, null, null)]);
        var template = await AddTemplateAsync(f, preset.Id, signatures: 1);

        Assert.Equal(dpo.Email, Assert.Single(await RecipientsAsync(f, await SubmitAsync(f, template, "1"))).Email);
    }

    [Fact]
    public async Task A_department_with_two_people_is_refused_and_names_who_they_are()
    {
        using var f = await FixtureWithOwnerAsync();
        await AddUserAsync(f, "Ines", department: "Legal");
        await AddUserAsync(f, "Omar", department: "Legal");
        var preset = await AddPresetAsync(f, WorkflowType.Sequential, id =>
            [WorkflowPresetStep.Create(id, 1, 1, "Legal", PresetApproverKind.Department, null, null, "Legal", null, null, null)]);
        var template = await AddTemplateAsync(f, preset.Id, signatures: 1);

        var ex = await Assert.ThrowsAsync<SignFlow.Application.Common.Exceptions.ValidationException>(() => SubmitAsync(f, template, "1"));

        Assert.Contains("2 active people", ex.Errors[string.Empty][0]);
        Assert.Contains("Ines", ex.Errors[string.Empty][0]);
        Assert.Empty(f.Db.Documents);
    }

    [Fact]
    public async Task A_named_person_who_has_since_been_deactivated_is_refused()
    {
        using var f = await FixtureWithOwnerAsync();
        var gone = await AddUserAsync(f, "Gone", active: false);
        var preset = await AddPresetAsync(f, WorkflowType.Sequential, id => [UserStep(id, 1, "Revue", gone.Id)]);
        var template = await AddTemplateAsync(f, preset.Id, signatures: 1);

        await Assert.ThrowsAsync<SignFlow.Application.Common.Exceptions.ValidationException>(() => SubmitAsync(f, template, "1"));
    }

    [Fact]
    public async Task When_every_step_is_skipped_the_request_is_refused_rather_than_sent_to_nobody()
    {
        using var f = await FixtureWithOwnerAsync();
        var director = await AddUserAsync(f, "Nour");
        var preset = await AddPresetAsync(f, WorkflowType.Sequential, id =>
            [UserStep(id, 1, "Direction", director.Id, conditionKey: "amount", op: ">", value: "10000")]);
        var template = await AddTemplateAsync(f, preset.Id, signatures: 1);

        await Assert.ThrowsAsync<SignFlow.Application.Common.Exceptions.ValidationException>(() => SubmitAsync(f, template, "5"));
    }

    [Fact]
    public async Task A_workflow_that_is_switched_off_blocks_new_requests()
    {
        using var f = await FixtureWithOwnerAsync();
        var legal = await AddUserAsync(f, "Sami");
        var preset = await AddPresetAsync(f, WorkflowType.Sequential, id => [UserStep(id, 1, "Revue", legal.Id)], active: false);
        var template = await AddTemplateAsync(f, preset.Id, signatures: 1);

        await Assert.ThrowsAsync<ConflictException>(() => SubmitAsync(f, template, "1"));
    }

    [Fact]
    public async Task Signature_fields_past_the_last_step_fall_back_to_the_requesters_choice()
    {
        using var f = await FixtureWithOwnerAsync();
        var legal = await AddUserAsync(f, "Sami");
        var preset = await AddPresetAsync(f, WorkflowType.Sequential, id => [UserStep(id, 1, "Revue", legal.Id)]);
        var template = await AddTemplateAsync(f, preset.Id, signatures: 2);

        var recipients = await RecipientsAsync(f, await SubmitAsync(f, template, "1", new ApproverSelection(null, "Externe", "ext@partner.com")));

        Assert.Equal(["sami@test.com", "ext@partner.com"], recipients.Select(r => r.Email));
        Assert.Null(recipients[1].RoleLabel);
    }

    [Fact]
    public async Task Without_a_workflow_the_template_behaves_as_before()
    {
        using var f = await FixtureWithOwnerAsync();
        var template = await AddTemplateAsync(f, null, signatures: 1);

        var recipients = await RecipientsAsync(f, await SubmitAsync(f, template, "1", new ApproverSelection(null, "Externe", "ext@partner.com")));

        Assert.Equal("ext@partner.com", Assert.Single(recipients).Email);
    }

    [Fact]
    public async Task A_mixed_workflow_renumbers_its_stages_when_a_step_in_the_middle_is_skipped()
    {
        using var f = await FixtureWithOwnerAsync();
        var a = await AddUserAsync(f, "Alpha");
        var b = await AddUserAsync(f, "Beta");
        var c = await AddUserAsync(f, "Gamma");
        var preset = await AddPresetAsync(f, WorkflowType.Mixed, id =>
        [
            UserStep(id, 1, "A", a.Id, stage: 1),
            UserStep(id, 2, "B", b.Id, stage: 2, conditionKey: "amount", op: ">", value: "1000"),
            UserStep(id, 3, "C", c.Id, stage: 3)
        ]);
        var template = await AddTemplateAsync(f, preset.Id, signatures: 3, type: WorkflowType.Mixed, stages: [1, 2, 3]);

        var recipients = await RecipientsAsync(f, await SubmitAsync(f, template, "10"));

        Assert.Equal(["alpha@test.com", "gamma@test.com"], recipients.Select(r => r.Email));
        Assert.Equal([1, 2], recipients.Select(r => r.Order));
    }

    [Fact]
    public async Task The_workflows_own_type_wins_over_the_templates()
    {
        using var f = await FixtureWithOwnerAsync();
        var a = await AddUserAsync(f, "Alpha");
        var b = await AddUserAsync(f, "Beta");
        var preset = await AddPresetAsync(f, WorkflowType.Parallel, id => [UserStep(id, 1, "A", a.Id), UserStep(id, 2, "B", b.Id)]);
        var template = await AddTemplateAsync(f, preset.Id, signatures: 2, type: WorkflowType.Sequential);

        var documentId = await SubmitAsync(f, template, "1");

        Assert.Equal([1, 1], (await RecipientsAsync(f, documentId)).Select(r => r.Order));
        Assert.Equal(WorkflowType.Parallel, (await f.Db.Documents.SingleAsync(d => d.Id == documentId)).WorkflowType);
    }
}

public class PdfAnnexRequestTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    /// <summary>Stands in for the browser renderer: records the HTML it was given and returns a real one-page PDF.</summary>
    private sealed class RecordingGenerator : IHtmlTemplatePdfGenerator
    {
        public List<string> Html { get; } = [];

        public Task<Stream> GenerateAsync(string renderedHtmlContent, string title, List<HtmlTemplatePdfSignature> signatures, CancellationToken ct = default)
        {
            Html.Add(renderedHtmlContent);
            return Task.FromResult<Stream>(new MemoryStream(OnePagePdf()));
        }
    }

    private static byte[] OnePagePdf()
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private sealed record World(
        ScenarioFixture F, InMemoryStorageService Storage, RecordingGenerator Generator, PdfPigService Pdf,
        PdfTemplate Template, Clause Approved, Clause Draft);

    private static async Task<World> SetUpAsync(bool templateCarriesApproved = false)
    {
        var f = new ScenarioFixture(OwnerId);
        var db = (ApplicationDbContext)f.Db;
        var users = f.GetHandler<UserManager<ApplicationUser>>();
        var approver = new ApplicationUser { Id = OwnerId, FirstName = "Owner", LastName = "Test", UserName = "owner@test.com", Email = "owner@test.com", IsActive = true };
        await users.CreateAsync(approver);

        var storage = new InMemoryStorageService();
        var basePdf = OnePagePdf();
        var path = await storage.UploadAsync(new MemoryStream(basePdf), "base.pdf", "application/pdf", "pdf-templates");

        var template = PdfTemplate.Create("Contrat", path, "base.pdf", basePdf.Length, 1, OwnerId, defaultApproverId: OwnerId);
        db.PdfTemplates.Add(template);
        db.PdfTemplateFields.AddRange(
            PdfTemplateField.Create(template.Id, "amount", "Montant", PdfFieldType.Number, PdfFieldFillRole.Requester, 1, 10, 10, 100, 20, false, 1),
            PdfTemplateField.Create(template.Id, "sig", "Signature", PdfFieldType.Signature, PdfFieldFillRole.Approver, 1, 10, 100, 150, 50, true, 2));

        var approved = Clause.Create("Confidentialité", ClauseCategory.Confidentiality,
            "<p>Les parties gardent le secret. Montant : {{amount}}.</p>", OwnerId, "fr");
        approved.SetStatus(ClauseStatus.Approved);
        var draft = Clause.Create("Brouillon", ClauseCategory.Confidentiality, "<p>Pas encore validé.</p>", OwnerId, "fr");
        db.Clauses.AddRange(approved, draft);

        if (templateCarriesApproved) template.SetAnnexClauses([approved.Id]);
        await db.SaveChangesAsync();

        return new World(f, storage, new RecordingGenerator(), new PdfPigService(NullLogger<PdfPigService>.Instance), template, approved, draft);
    }

    private static Task<Guid> SubmitAsync(World w, params Guid[] clauseIds)
    {
        var handler = new CreateRequestFromPdfTemplateCommandHandler(
            w.F.Db, w.Storage, w.Pdf, w.Generator, w.F.GetHandler<IWorkflowEngine>(), w.F.GetHandler<IWorkflowPresetResolver>(),
            w.F.GetHandler<ICurrentUserService>(), w.F.GetHandler<IAuditService>(),
            NullLogger<CreateRequestFromPdfTemplateCommandHandler>.Instance);

        return handler.HandleAsync(new CreateRequestFromPdfTemplateCommand(
            w.Template.Id, new Dictionary<string, string> { ["amount"] = "750" }, null, "Contrat test", null,
            clauseIds.Length == 0 ? null : [.. clauseIds]));
    }

    private static async Task<int> StoredPageCountAsync(World w, Guid documentId)
    {
        var path = (await w.F.Db.Documents.SingleAsync(d => d.Id == documentId)).StoragePath;
        await using var stream = await w.Storage.DownloadAsync(path);
        return await w.Pdf.GetPageCountAsync(stream);
    }

    [Fact]
    public async Task A_request_without_an_annex_is_a_plain_copy_of_the_template_file()
    {
        using var w0 = (await SetUpAsync()).F;
        var w = await SetUpAsync();
        using var _ = w.F;

        var documentId = await SubmitAsync(w);

        Assert.Equal(1, (await w.F.Db.Documents.SingleAsync(d => d.Id == documentId)).PageCount);
        Assert.Equal(1, await StoredPageCountAsync(w, documentId));
        Assert.Empty(w.Generator.Html);
    }

    [Fact]
    public async Task A_clause_the_requester_adds_is_appended_as_real_pages_after_the_contract()
    {
        var w = await SetUpAsync();
        using var _ = w.F;

        var documentId = await SubmitAsync(w, w.Approved.Id);

        var document = await w.F.Db.Documents.SingleAsync(d => d.Id == documentId);
        Assert.Equal(2, document.PageCount);
        Assert.Equal(2, await StoredPageCountAsync(w, documentId));
        Assert.True(document.FileSizeBytes > 0);

        var html = System.Net.WebUtility.HtmlDecode(Assert.Single(w.Generator.Html));
        Assert.Contains("Confidentialité", html);
        Assert.Contains("Les parties gardent le secret. Montant : 750.", html);
    }

    [Fact]
    public async Task Using_a_clause_counts_towards_its_usage()
    {
        var w = await SetUpAsync();
        using var _ = w.F;

        await SubmitAsync(w, w.Approved.Id);

        Assert.Equal(1, (await w.F.Db.Clauses.SingleAsync(c => c.Id == w.Approved.Id)).UsageCount);
    }

    [Fact]
    public async Task A_draft_clause_cannot_travel_with_a_contract()
    {
        var w = await SetUpAsync();
        using var _ = w.F;

        await Assert.ThrowsAsync<SignFlow.Application.Common.Exceptions.ValidationException>(() => SubmitAsync(w, w.Draft.Id));
        Assert.Empty(w.F.Db.Documents);
    }

    [Fact]
    public async Task The_templates_own_annex_is_added_without_the_requester_asking_and_never_twice()
    {
        var w = await SetUpAsync(templateCarriesApproved: true);
        using var _ = w.F;

        var documentId = await SubmitAsync(w, w.Approved.Id); // the requester picks the same clause again

        Assert.Equal(2, await StoredPageCountAsync(w, documentId));
        var html = Assert.Single(w.Generator.Html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(System.Net.WebUtility.HtmlDecode(html), "<h2>Confidentialité</h2>"));
    }

    [Fact]
    public async Task A_clause_the_template_carries_stays_even_after_it_is_deprecated()
    {
        var w = await SetUpAsync(templateCarriesApproved: true);
        using var _ = w.F;
        w.Approved.SetStatus(ClauseStatus.Deprecated);
        await w.F.Db.SaveChangesAsync();

        var documentId = await SubmitAsync(w);

        Assert.Equal(2, await StoredPageCountAsync(w, documentId));
    }

    [Fact]
    public async Task A_merge_that_produces_nothing_stops_the_request_instead_of_storing_an_empty_contract()
    {
        var w = await SetUpAsync();
        using var _ = w.F;
        var handler = new CreateRequestFromPdfTemplateCommandHandler(
            w.F.Db, w.Storage, new NullPdfService(), w.Generator, w.F.GetHandler<IWorkflowEngine>(), w.F.GetHandler<IWorkflowPresetResolver>(),
            w.F.GetHandler<ICurrentUserService>(), w.F.GetHandler<IAuditService>(),
            NullLogger<CreateRequestFromPdfTemplateCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new CreateRequestFromPdfTemplateCommand(
            w.Template.Id, new Dictionary<string, string> { ["amount"] = "1" }, null, "x", null, [w.Approved.Id])));
        Assert.Empty(w.F.Db.Documents);
    }

    [Fact]
    public async Task Merging_joins_the_files_end_to_end_in_the_order_given()
    {
        var pdf = new PdfPigService(NullLogger<PdfPigService>.Instance);

        await using var merged = await pdf.MergePdfsAsync([new MemoryStream(OnePagePdf()), new MemoryStream(OnePagePdf()), new MemoryStream(OnePagePdf())]);

        Assert.Equal(3, await pdf.GetPageCountAsync(merged));
    }
}

public class TemplateReviewTests
{
    private static (ScenarioFixture Fixture, ReviewHtmlTemplateCommandHandler Handler) Setup(string[]? roles = null)
    {
        var fixture = new ScenarioFixture(Guid.NewGuid());
        ((FakeCurrentUserService)fixture.GetHandler<ICurrentUserService>()).RolesOverride = roles;
        return (fixture, new ReviewHtmlTemplateCommandHandler(fixture.Db, fixture.GetHandler<IContractReviewService>(),
            fixture.GetHandler<ICurrentUserService>()));
    }

    private static readonly ReviewHtmlTemplateCommand Command = new(
        "<p>{{party}} paie {{amount}}.</p>{{#if amount > 1000}}<p>Garantie.</p>{{#else}}<p>Sans garantie.</p>{{/if}}<div>{{sig}}</div>",
        [new ReviewTemplateField("party", "Partie", "Text", "Initiator"), new ReviewTemplateField("amount", "Montant", "Number", "Initiator"),
         new ReviewTemplateField("sig", "Signature", "Signature", "Approver")]);

    [Fact]
    public async Task Harvey_reads_the_template_with_gaps_labelled_and_every_conditional_clause_included()
    {
        var (fixture, handler) = Setup();
        using var _ = fixture;
        fixture.Harvey.Findings = [FakeHarvey.Finding("liability", "High", "Article 1")];

        var result = await handler.HandleAsync(Command);

        var text = string.Join(" ", fixture.Harvey.ClausesSent.Select(c => c.Text));
        Assert.Contains("«Partie» paie «Montant».", text);
        Assert.Contains("Garantie.", text);
        Assert.Contains("Sans garantie.", text);
        Assert.DoesNotContain("{{", text);
        Assert.Equal(10, result.RiskScore);
        Assert.Equal(("Risque liability", "liability"), (Assert.Single(result.Findings).ClauseType, result.Findings[0].RuleKey));
    }

    [Fact]
    public async Task A_review_that_could_not_run_is_refused_instead_of_reading_as_no_problems()
    {
        var (fixture, handler) = Setup();
        using var _ = fixture;
        fixture.Harvey.Down = true;

        var ex = await Assert.ThrowsAsync<ServiceUnavailableException>(() => handler.HandleAsync(Command));

        Assert.Contains("Harvey", ex.Message);
        Assert.Equal(ReviewRun.Unavailable, ex.Code);
    }

    [Fact]
    public async Task Only_administrators_and_managers_may_review_a_template()
    {
        var (fixture, handler) = Setup(["Employee"]);
        using var _ = fixture;
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => handler.HandleAsync(Command));
    }

    [Fact]
    public async Task An_empty_template_is_refused()
    {
        var (fixture, handler) = Setup();
        using var _ = fixture;
        await Assert.ThrowsAsync<SignFlow.Application.Common.Exceptions.ValidationException>(() =>
            handler.HandleAsync(new ReviewHtmlTemplateCommand("  ", [])));
    }
}
