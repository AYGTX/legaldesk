using SignFlow.Application.Common.Services;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Documents.Commands;
using SignFlow.Application.Features.Documents.Queries;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// A reviewer asks for changes on a contract built from a template. The owner may only change the template's
/// answers — never the PDF, the layout or the circuit — and the version the reviewer sent back must stay
/// available, with its answers, so the reviewer can see what changed.
/// </summary>
public class TemplateRevisionTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private sealed record Seed(
        ScenarioFixture Fixture, Document Document, Recipient Reviewer, SignatureField ReviewerSignature,
        FakeCurrentUserService User);

    /// <summary>An HTML template contract with a reviewer who has asked for changes on version 1.</summary>
    private static async Task<Seed> SeedHtmlContractAsync()
    {
        var fixture = new ScenarioFixture(OwnerId);
        var db = (ApplicationDbContext)fixture.Db;

        db.Set<ApplicationUser>().Add(new ApplicationUser
        {
            Id = OwnerId, Email = "owner@test.com", UserName = "owner@test.com", FirstName = "Test", LastName = "Owner",
            IsActive = true, SecurityStamp = Guid.NewGuid().ToString()
        });

        var template = HtmlTemplate.Create("Service agreement",
            "<p>Client {{client}} pays {{amount}} TND.</p>{{reviewerSignature}}", OwnerId);
        db.HtmlTemplates.Add(template);
        db.HtmlTemplateFields.AddRange(
            HtmlTemplateField.Create(template.Id, "client", "Client", HtmlTemplateFieldType.Text, HtmlTemplateFieldRole.Initiator, true, 1),
            HtmlTemplateField.Create(template.Id, "amount", "Amount", HtmlTemplateFieldType.Number, HtmlTemplateFieldRole.Initiator, true, 2),
            HtmlTemplateField.Create(template.Id, "owner_name", "Owner", HtmlTemplateFieldType.Text, HtmlTemplateFieldRole.Initiator, false, 3, autoFillFrom: "user.fullName"),
            HtmlTemplateField.Create(template.Id, "reviewerSignature", "Reviewer signature", HtmlTemplateFieldType.Signature, HtmlTemplateFieldRole.Approver, true, 4));

        var values = new Dictionary<string, string> { ["client"] = "Contoso", ["amount"] = "100", ["owner_name"] = "Test Owner" };
        var document = Document.Create("Service agreement", "Service agreement.html", $"html-templates/{template.Id}", 0, 1, OwnerId);
        document.SetHtmlSource(template.Id, JsonSerializer.Serialize(values), template.HtmlContent);
        db.Documents.Add(document);

        var reviewer = Recipient.Create(document.Id, "Rita Reviewer", "rita@test.com", 1, ApprovalType.Signature);
        db.Recipients.Add(reviewer);
        var signature = SignatureField.Create(document.Id, FieldType.Signature, 1, 0, 0, 200, 80, reviewer.Id);
        db.SignatureFields.Add(signature);
        await db.SaveChangesAsync();

        // The circuit runs, the reviewer signs a field, then sends the contract back.
        await fixture.GetHandler<IWorkflowEngine>().StartAsync(document.Id);
        signature.SetSigned("signed");
        reviewer.RequestChanges("The amount is wrong, it should include VAT.");
        document.SeedStatus(DocumentStatus.ChangesRequested);
        await db.SaveChangesAsync();

        return new Seed(fixture, document, reviewer, signature, (FakeCurrentUserService)fixture.GetHandler<ICurrentUserService>());
    }

    private static ReviseFromTemplateCommandHandler ReviseHandler(ScenarioFixture f) => new(
        f.Db, f.GetHandler<IWorkflowEngine>(), f.GetHandler<ICurrentUserService>(),
        f.GetHandler<IAuditService>(), NullLogger<ReviseFromTemplateCommandHandler>.Instance);

    private static GetTemplateRevisionFormQueryHandler FormHandler(ScenarioFixture f) =>
        new(f.Db, f.GetHandler<ICurrentUserService>());

    [Fact]
    public async Task The_form_offers_only_the_answers_the_owner_may_change()
    {
        using var seed = (await SeedHtmlContractAsync()).Fixture;
        var documentId = (await seed.Db.Documents.SingleAsync()).Id;

        var form = await FormHandler(seed).HandleAsync(new GetTemplateRevisionFormQuery(documentId));

        // Not the auto-filled owner name, not the approver's signature.
        Assert.Equal(["client", "amount"], form.Fields.Select(f => f.FieldKey));
        Assert.Equal("Contoso", form.Values["client"]);
        Assert.Equal("Html", form.TemplateKind);
        var request = Assert.Single(form.ChangeRequests);
        Assert.Equal("Rita Reviewer", request.RequestedBy);
        Assert.Contains("VAT", request.Reason);
    }

    [Fact]
    public async Task Revising_keeps_the_old_version_with_its_answers_and_sends_the_circuit_again()
    {
        var seed = await SeedHtmlContractAsync();
        using var fixture = seed.Fixture;

        var result = await ReviseHandler(fixture).HandleAsync(new ReviseFromTemplateCommand(
            seed.Document.Id, new Dictionary<string, string> { ["amount"] = "120" }, null));

        Assert.Equal(2, result.Version);

        var document = await fixture.Db.Documents.Include(d => d.Versions).Include(d => d.Recipients).SingleAsync();
        Assert.Equal(2, document.Version);
        Assert.Equal(DocumentStatus.InProgress, document.Status);   // sent to the reviewer again, not parked as a draft
        Assert.Equal("120", TemplateAnswers(document.FilledValuesJson)["amount"]);
        Assert.Equal("Contoso", TemplateAnswers(document.FilledValuesJson)["client"]);   // untouched answers survive

        // Version 1 is still there, with the answers it had.
        var kept = Assert.Single(document.Versions);
        Assert.Equal(1, kept.VersionNumber);
        Assert.Equal("100", TemplateAnswers(kept.FilledValuesJson)["amount"]);
        Assert.Contains("Amount", kept.ChangeNote);

        // The reviewer starts a fresh round: their change request is cleared and the old signature is void.
        Assert.NotEqual(WorkflowStepStatus.ChangesRequested, document.Recipients.Single().Status);
        Assert.False((await fixture.Db.SignatureFields.SingleAsync()).IsSigned);
    }

    [Fact]
    public async Task The_detail_shows_what_changed_between_the_versions()
    {
        var seed = await SeedHtmlContractAsync();
        using var fixture = seed.Fixture;
        await ReviseHandler(fixture).HandleAsync(new ReviseFromTemplateCommand(
            seed.Document.Id, new Dictionary<string, string> { ["amount"] = "120", ["client"] = "Fabrikam" }, "VAT added"));

        var detail = await new GetDocumentByIdQueryHandler(fixture.Db, fixture.GetHandler<ICurrentUserService>())
            .HandleAsync(new GetDocumentByIdQuery(seed.Document.Id));

        Assert.Equal("Html", detail.TemplateKind);
        var revision = Assert.Single(detail.TemplateRevisions!);
        Assert.Equal((1, 2), (revision.FromVersion, revision.ToVersion));
        Assert.Equal("VAT added", revision.ChangeNote);
        Assert.Equal(
            [("Client", "Contoso", "Fabrikam"), ("Amount", "100", "120")],
            revision.Changes.Select(c => (c.Label, c.OldValue, c.NewValue)));
    }

    [Fact]
    public async Task A_revision_that_changes_nothing_is_refused()
    {
        var seed = await SeedHtmlContractAsync();
        using var fixture = seed.Fixture;

        await Assert.ThrowsAsync<ConflictException>(() => ReviseHandler(fixture).HandleAsync(new ReviseFromTemplateCommand(
            seed.Document.Id, new Dictionary<string, string> { ["amount"] = "100" }, null)));
    }

    [Fact]
    public async Task Answers_the_owner_may_not_change_are_ignored()
    {
        var seed = await SeedHtmlContractAsync();
        using var fixture = seed.Fixture;

        // Only an auto-filled value and an unknown key are submitted: nothing the owner may change differs.
        await Assert.ThrowsAsync<ConflictException>(() => ReviseHandler(fixture).HandleAsync(new ReviseFromTemplateCommand(
            seed.Document.Id,
            new Dictionary<string, string> { ["owner_name"] = "Someone Else", ["injected"] = "x" }, null)));

        Assert.Equal("Test Owner", TemplateAnswers((await fixture.Db.Documents.SingleAsync()).FilledValuesJson)["owner_name"]);
    }

    [Fact]
    public async Task A_required_answer_cannot_be_emptied()
    {
        var seed = await SeedHtmlContractAsync();
        using var fixture = seed.Fixture;

        await Assert.ThrowsAsync<ValidationException>(() => ReviseHandler(fixture).HandleAsync(new ReviseFromTemplateCommand(
            seed.Document.Id, new Dictionary<string, string> { ["client"] = "  " }, null)));
    }

    [Fact]
    public async Task Only_the_owner_can_revise_and_only_after_changes_were_requested()
    {
        var seed = await SeedHtmlContractAsync();
        using var fixture = seed.Fixture;
        var command = new ReviseFromTemplateCommand(seed.Document.Id, new Dictionary<string, string> { ["amount"] = "120" }, null);

        // Someone else.
        seed.User.UserIdOverride = Guid.NewGuid();
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => ReviseHandler(fixture).HandleAsync(command));
        seed.User.UserIdOverride = null;

        // Not in a change-request state any more.
        var document = await fixture.Db.Documents.SingleAsync();
        document.SeedStatus(DocumentStatus.InProgress);
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => ReviseHandler(fixture).HandleAsync(command));
    }

    [Fact]
    public async Task A_pdf_template_contract_writes_the_new_answer_into_the_field_the_template_placed()
    {
        var fixture = new ScenarioFixture(OwnerId);
        using var _ = fixture;
        var db = (ApplicationDbContext)fixture.Db;

        var template = PdfTemplate.Create("Leave request", "pdf-templates/x.pdf", "x.pdf", 10, 1, OwnerId);
        db.PdfTemplates.Add(template);
        db.PdfTemplateFields.AddRange(
            PdfTemplateField.Create(template.Id, "days", "Days", PdfFieldType.Number, PdfFieldFillRole.Requester, 1, 100, 200, 80, 20, true, 1),
            PdfTemplateField.Create(template.Id, "reason", "Reason", PdfFieldType.Text, PdfFieldFillRole.Requester, 1, 100, 260, 200, 20, true, 2),
            PdfTemplateField.Create(template.Id, "sig", "Manager", PdfFieldType.Signature, PdfFieldFillRole.Approver, 1, 100, 400, 200, 60, true, 3));

        var document = Document.Create("Leave request", "x.pdf", "documents/x.pdf", 10, 1, OwnerId);
        document.SetPdfTemplateSource(template.Id, JsonSerializer.Serialize(new Dictionary<string, string> { ["days"] = "5", ["reason"] = "Holiday" }));
        db.Documents.Add(document);
        var manager = Recipient.Create(document.Id, "Mia Manager", "mia@test.com", 1, ApprovalType.Signature);
        db.Recipients.Add(manager);

        var days = SignatureField.Create(document.Id, FieldType.Number, 1, 100, 200, 80, 20, null);
        days.SetSigned("5");
        var reason = SignatureField.Create(document.Id, FieldType.Text, 1, 100, 260, 200, 20, null);
        reason.SetSigned("Holiday");
        var sig = SignatureField.Create(document.Id, FieldType.Signature, 1, 100, 400, 200, 60, manager.Id);
        db.SignatureFields.AddRange(days, reason, sig);
        await db.SaveChangesAsync();

        await fixture.GetHandler<IWorkflowEngine>().StartAsync(document.Id);
        manager.RequestChanges("Too many days");
        document.SeedStatus(DocumentStatus.ChangesRequested);
        await db.SaveChangesAsync();

        await ReviseHandler(fixture).HandleAsync(new ReviseFromTemplateCommand(
            document.Id, new Dictionary<string, string> { ["days"] = "3" }, null));

        var fields = await db.SignatureFields.ToListAsync();
        Assert.Equal("3", fields.Single(f => f.Id == days.Id).SignedValue);
        Assert.True(fields.Single(f => f.Id == days.Id).IsSigned);          // still stamped, with the new value
        Assert.Equal("Holiday", fields.Single(f => f.Id == reason.Id).SignedValue);   // untouched
        Assert.Equal(2, (await db.Documents.SingleAsync()).Version);
    }

    [Fact]
    public async Task A_template_contract_cannot_be_revised_by_replacing_its_pdf()
    {
        var fixture = new ScenarioFixture(OwnerId);
        using var _ = fixture;
        var db = (ApplicationDbContext)fixture.Db;

        var document = Document.Create("Leave request", "x.pdf", "documents/x.pdf", 10, 1, OwnerId);
        document.SetPdfTemplateSource(Guid.NewGuid(), "{}");
        document.SeedStatus(DocumentStatus.ChangesRequested);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var handler = new UploadRevisionCommandHandler(
            fixture.Db, fixture.GetHandler<IStorageService>(), fixture.GetHandler<IPdfService>(),
            fixture.GetHandler<PdfUploadGuard>(), fixture.GetHandler<ICurrentUserService>(),
            fixture.GetHandler<IAuditService>(), NullLogger<UploadRevisionCommandHandler>.Instance);

        await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(new UploadRevisionCommand(
            document.Id, new MemoryStream(NullPdfInspector.FakePdf), "x.pdf", NullPdfInspector.FakePdf.Length, "application/pdf", null)));
    }

    private static Dictionary<string, string> TemplateAnswers(string? json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json ?? "{}")!;
}
