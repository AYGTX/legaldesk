using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.HtmlTemplates.Commands;
using SignFlow.Application.Features.Templates;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// Template versions (R-M6-5, R-M6-6): only an approved version makes contracts, approval needs Harvey's review without
/// a critical finding, an approved version is changed through its next version, and a request is never sent before
/// its pre-flight review.
/// </summary>
public class TemplateLifecycleTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private static async Task<(ScenarioFixture F, HtmlTemplate Template)> SeedAsync()
    {
        var f = new ScenarioFixture(OwnerId);
        f.Db.Users.Add(new ApplicationUser
        {
            Id = OwnerId, Email = "owner@test.com", UserName = "owner@test.com", FirstName = "Test", LastName = "Owner",
            IsActive = true, SecurityStamp = Guid.NewGuid().ToString()
        });
        var template = HtmlTemplate.Create("Prestation", "<h2>Article 1</h2><p>Le prix est {{price}}.</p>{{sig}}", OwnerId);
        f.Db.HtmlTemplates.Add(template);
        f.Db.HtmlTemplateFields.AddRange(
            HtmlTemplateField.Create(template.Id, "price", "Prix", HtmlTemplateFieldType.Text, HtmlTemplateFieldRole.Initiator, true, 1),
            HtmlTemplateField.Create(template.Id, "sig", "Signature", HtmlTemplateFieldType.Signature, HtmlTemplateFieldRole.Approver, true, 2));
        await f.Db.SaveChangesAsync();
        return (f, template);
    }

    private static CreateRequestFromHtmlTemplateCommandHandler Requests(ScenarioFixture f) => new(
        f.Db, f.GetHandler<IWorkflowEngine>(), f.GetHandler<IWorkflowPresetResolver>(), f.GetHandler<ICurrentUserService>(),
        f.GetHandler<IAuditService>(), NullLogger<CreateRequestFromHtmlTemplateCommandHandler>.Instance);

    private static Task<Guid> RequestAsync(ScenarioFixture f, Guid templateId) => Requests(f).HandleAsync(
        new CreateRequestFromHtmlTemplateCommand(templateId, new() { ["price"] = "1000 TND" },
            [new ApproverSelection(null, "Ext Signer", "ext@partner.com")], "Contrat"));

    private static ApproveTemplateCommandHandler Approver(ScenarioFixture f) => new(
        f.Db, f.GetHandler<ICurrentUserService>(), f.GetHandler<IContractReviewService>(), f.GetHandler<IStorageService>(),
        f.GetHandler<IPdfService>(), NullLogger<ApproveTemplateCommandHandler>.Instance);

    private static Task<TemplateVersionDto> SubmitAsync(ScenarioFixture f, Guid id) =>
        new SubmitTemplateCommandHandler(f.Db, f.GetHandler<ICurrentUserService>()).HandleAsync(new SubmitTemplateCommand(TemplateKind.Html, id));

    [Fact]
    public async Task A_draft_makes_no_contract_and_an_approved_version_does_without_notifying_anyone_before_review()
    {
        var (f, template) = await SeedAsync();
        using var _ = f;

        var refused = await Assert.ThrowsAsync<ConflictException>(() => RequestAsync(f, template.Id));
        Assert.Equal("template.notApproved", refused.Code);

        await SubmitAsync(f, template.Id);
        await Approver(f).HandleAsync(new ApproveTemplateCommand(TemplateKind.Html, template.Id));
        var documentId = await RequestAsync(f, template.Id);

        var document = await f.Db.Documents.SingleAsync(d => d.Id == documentId);
        Assert.Equal(DocumentStatus.Draft, document.Status);
        Assert.Equal(1, document.TemplateVersion);
        Assert.Empty(((NullEmailService)f.GetHandler<IEmailService>()).Sent);
    }

    [Fact]
    public async Task An_open_critical_finding_refuses_the_approval()
    {
        var (f, template) = await SeedAsync();
        using var _ = f;
        await SubmitAsync(f, template.Id);
        f.Harvey.Findings = [FakeHarvey.Finding("liability.unlimited", "Critical", "Article 1")];

        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            Approver(f).HandleAsync(new ApproveTemplateCommand(TemplateKind.Html, template.Id)));

        Assert.Equal("template.criticalFindings", refused.Code);
        Assert.Equal(TemplateStatus.InLegalReview, (await f.Db.HtmlTemplates.SingleAsync()).Status);
    }

    [Fact]
    public async Task An_approved_version_is_changed_through_its_next_version_which_retires_it_once_approved()
    {
        var (f, template) = await SeedAsync();
        using var _ = f;
        await SubmitAsync(f, template.Id);
        await Approver(f).HandleAsync(new ApproveTemplateCommand(TemplateKind.Html, template.Id));

        var next = await new DraftNextTemplateVersionCommandHandler(f.Db, f.GetHandler<ICurrentUserService>())
            .HandleAsync(new DraftNextTemplateVersionCommand(TemplateKind.Html, template.Id));
        Assert.Equal(2, next.Version);
        Assert.Equal(TemplateStatus.Draft, next.Status);
        Assert.Equal(2, await f.Db.HtmlTemplateFields.CountAsync(x => x.HtmlTemplateId == next.Id));

        // Until v2 is approved, v1 still makes contracts.
        var fromV1 = await RequestAsync(f, template.Id);
        Assert.Equal(1, (await f.Db.Documents.SingleAsync(d => d.Id == fromV1)).TemplateVersion);

        await SubmitAsync(f, next.Id);
        await Approver(f).HandleAsync(new ApproveTemplateCommand(TemplateKind.Html, next.Id));

        Assert.Equal(TemplateStatus.Retired, (await f.Db.HtmlTemplates.SingleAsync(t => t.Id == template.Id)).Status);
        var refused = await Assert.ThrowsAsync<ConflictException>(() => RequestAsync(f, template.Id));
        Assert.Equal("template.notApproved", refused.Code);
        var fromV2 = await RequestAsync(f, next.Id);
        Assert.Equal(2, (await f.Db.Documents.SingleAsync(d => d.Id == fromV2)).TemplateVersion);
    }

    [Fact]
    public void A_retired_or_approved_template_cannot_be_edited_in_place()
    {
        var template = HtmlTemplate.Create("Prestation", "<p>x</p>", OwnerId);
        template.ImportApproved();

        Assert.Throws<InvalidOperationException>(() =>
            template.Update("Prestation", "<p>y</p>", null, null, true, WorkflowType.Sequential, false, null));
    }
}
