using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.HtmlTemplates.Commands;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;
using SignFlow.Infrastructure.Services.Pdf;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// The things a PDF template could do that an HTML template could not, and the other way round: a dropdown, a default
/// value and the request-level auto-fill sources on HTML templates; several lines of text on PDF templates.
/// </summary>
public class TemplateFieldGapsTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private const string Choices = "[\"Annuel\",\"Maladie\"]";

    private static async Task<(ScenarioFixture Fixture, HtmlTemplate Template)> SeedHtmlTemplateAsync()
    {
        var fixture = new ScenarioFixture(OwnerId);
        var db = (ApplicationDbContext)fixture.Db;

        db.Set<ApplicationUser>().Add(new ApplicationUser
        {
            Id = OwnerId, Email = "owner@test.com", UserName = "owner@test.com", FirstName = "Test", LastName = "Owner",
            IsActive = true, SecurityStamp = Guid.NewGuid().ToString()
        });

        var template = HtmlTemplate.Create("Demande", "<p>{{kind}} / {{country}} / {{signed_on}} / {{subject}}</p>{{sig}}", OwnerId);

        template.ImportApproved();
        db.HtmlTemplates.Add(template);
        db.HtmlTemplateFields.AddRange(
            HtmlTemplateField.Create(template.Id, "kind", "Type", HtmlTemplateFieldType.Dropdown, HtmlTemplateFieldRole.Initiator, true, 1, optionsJson: Choices),
            HtmlTemplateField.Create(template.Id, "country", "Pays", HtmlTemplateFieldType.Text, HtmlTemplateFieldRole.Initiator, true, 2, defaultValue: "Tunisie"),
            HtmlTemplateField.Create(template.Id, "signed_on", "Date", HtmlTemplateFieldType.Date, HtmlTemplateFieldRole.Initiator, true, 3, autoFillFrom: "system.today"),
            HtmlTemplateField.Create(template.Id, "subject", "Objet", HtmlTemplateFieldType.Text, HtmlTemplateFieldRole.Initiator, false, 4, autoFillFrom: "request.title"),
            HtmlTemplateField.Create(template.Id, "sig", "Signature", HtmlTemplateFieldType.Signature, HtmlTemplateFieldRole.Approver, true, 5));
        await db.SaveChangesAsync();
        return (fixture, template);
    }

    private static CreateRequestFromHtmlTemplateCommandHandler Handler(ScenarioFixture f) => new(
        f.Db, f.GetHandler<IWorkflowEngine>(), f.GetHandler<IWorkflowPresetResolver>(), f.GetHandler<ICurrentUserService>(),
        f.GetHandler<IAuditService>(), NullLogger<CreateRequestFromHtmlTemplateCommandHandler>.Instance);

    private static CreateRequestFromHtmlTemplateCommand Request(Guid templateId, Dictionary<string, string> values) =>
        new(templateId, values, [new ApproverSelection(null, "Ext Signer", "ext@partner.com")], "Contrat Contoso");

    [Fact]
    public async Task A_blank_answer_takes_the_default_and_the_app_fills_the_date_and_the_subject()
    {
        var (fixture, template) = await SeedHtmlTemplateAsync();
        using var _ = fixture;

        // "country" (required, default Tunisie) and "signed_on" (required, auto-filled) are not sent at all.
        var id = await Handler(fixture).HandleAsync(Request(template.Id, new() { ["kind"] = "Annuel" }));

        var answers = TemplateRevisionSupport.ParseValues((await fixture.Db.Documents.SingleAsync(d => d.Id == id)).FilledValuesJson);
        Assert.Equal("Tunisie", answers["country"]);
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), answers["signed_on"]);
        Assert.Equal("Contrat Contoso", answers["subject"]);
    }

    [Fact]
    public async Task An_answer_the_initiator_gives_beats_the_default()
    {
        var (fixture, template) = await SeedHtmlTemplateAsync();
        using var _ = fixture;

        var id = await Handler(fixture).HandleAsync(Request(template.Id, new() { ["kind"] = "Maladie", ["country"] = "France" }));

        var answers = TemplateRevisionSupport.ParseValues((await fixture.Db.Documents.SingleAsync(d => d.Id == id)).FilledValuesJson);
        Assert.Equal("France", answers["country"]);
        Assert.Equal("Maladie", answers["kind"]);
    }

    [Fact]
    public async Task A_dropdown_answer_outside_its_choices_is_refused()
    {
        var (fixture, template) = await SeedHtmlTemplateAsync();
        using var _ = fixture;

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Handler(fixture).HandleAsync(Request(template.Id, new() { ["kind"] = "Vacances" })));

        Assert.Contains("Type", ex.Message);
        Assert.Empty(await fixture.Db.Documents.ToListAsync());
    }

    [Fact]
    public async Task A_required_dropdown_left_blank_is_still_required()
    {
        var (fixture, template) = await SeedHtmlTemplateAsync();
        using var _ = fixture;

        await Assert.ThrowsAsync<ValidationException>(() => Handler(fixture).HandleAsync(Request(template.Id, new())));
    }

    [Fact]
    public async Task A_dropdown_compares_like_text_in_a_conditional_clause()
    {
        var html = "{{#if kind == \"Maladie\"}}Certificat médical requis.{{/if}}";
        var types = new Dictionary<string, HtmlTemplateFieldType> { ["kind"] = HtmlTemplateFieldType.Dropdown };

        Assert.Equal("Certificat médical requis.", HtmlContractRenderer.Render(html, new Dictionary<string, string> { ["kind"] = "Maladie" }, types));
        Assert.Equal(string.Empty, HtmlContractRenderer.Render(html, new Dictionary<string, string> { ["kind"] = "Annuel" }, types));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_multi_line_pdf_field_wraps_inside_its_box_instead_of_running_off_it()
    {
        PdfSharp.Fonts.GlobalFontSettings.FontResolver ??= new AppFontResolver();
        var storage = new InMemoryStorageService();

        // A blank one-page PDF.
        using var blank = new PdfSharp.Pdf.PdfDocument();
        blank.AddPage().Size = PdfSharp.PageSize.A4;
        var source = new MemoryStream();
        blank.Save(source, closeStream: false);
        var path = await storage.UploadAsync(source, "blank.pdf", "application/pdf", "docs");

        const string sentence = "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda omega";
        var service = new PdfStampService(storage, NullLogger<PdfStampService>.Instance);

        // A box 120 pt wide (points, R-M3-1): far narrower than the sentence on one line.
        await using var stamped = await service.StampAsync(path,
            [new StampFieldDto(FieldType.MultilineText, 1, 60, 60, 120, 67, sentence, null)]);

        stamped.Position = 0;
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(stamped);
        var words = pdf.GetPage(1).GetWords().ToList();
        var text = string.Join(" ", words.Select(w => w.Text));

        Assert.Contains("alpha", text);
        Assert.Contains("omega", text);              // the end of the sentence is there: it wrapped, it was not clipped

        // The box runs from 60 to 180 pt from the page edge. Every word sits inside it, which a single line of this
        // sentence never could.
        Assert.All(words, w => Assert.True(w.BoundingBox.Right <= 180 + 2, $"'{w.Text}' ends at {w.BoundingBox.Right:0.#} pt"));
        Assert.True(words.Select(w => Math.Round(w.BoundingBox.Bottom)).Distinct().Count() > 1, "expected the words on several lines");
    }
}
