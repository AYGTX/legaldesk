using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.Documents.Commands;
using SignFlow.Application.Features.Documents.Queries;
using SignFlow.Application.Features.Reviews;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// A contract built from an HTML template has no PDF to read, but it is exactly as reviewable as one: the AI is given the
/// template as the signer sees it, with the answers filled in and the conditional clauses resolved.
/// </summary>
public class HtmlContractReviewTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    /// <summary>Records what the language model is handed (the summary only: the review never uses a model).</summary>
    private sealed class CapturingAiService : NullAiService, IAiService
    {
        public List<string> Texts { get; } = [];

        public new Task<string> SummarizeDocumentAsync(string text, string title, CancellationToken ct = default)
        { Texts.Add(text); return Task.FromResult("A short summary."); }
    }

    private sealed record Seed(ScenarioFixture Fixture, Document Document, CapturingAiService Ai, ContractTextProvider Text);

    private static async Task<Seed> SeedAsync(string amount, bool personalData)
    {
        var fixture = new ScenarioFixture(OwnerId);
        var db = (ApplicationDbContext)fixture.Db;

        const string html = """
            <h2>Article 1 — Prix</h2><p>Le Client <strong>{{client}}</strong> paie {{amount}} TND.</p>
            {{#if personal_data}}<h2>Article 2 — Données personnelles</h2><p>Les parties respectent la loi 2004-63.</p>{{/if}}
            {{#if amount >= 100000}}<h2>Article 3 — Garantie</h2><p>Une garantie bancaire est exigée.</p>{{/if}}
            <p>{{clientSignature}}</p>
            """;

        var template = HtmlTemplate.Create("Contrat", html, OwnerId);

        template.ImportApproved();
        db.HtmlTemplates.Add(template);
        db.HtmlTemplateFields.AddRange(
            HtmlTemplateField.Create(template.Id, "client", "Client", HtmlTemplateFieldType.Text, HtmlTemplateFieldRole.Initiator, true, 1),
            HtmlTemplateField.Create(template.Id, "amount", "Montant", HtmlTemplateFieldType.Number, HtmlTemplateFieldRole.Initiator, true, 2),
            HtmlTemplateField.Create(template.Id, "personal_data", "Données personnelles", HtmlTemplateFieldType.Checkbox, HtmlTemplateFieldRole.Initiator, false, 3));

        var values = new Dictionary<string, string>
        {
            ["client"] = "Smith & Sons", ["amount"] = amount, ["personal_data"] = personalData ? "true" : "false"
        };
        var document = Document.Create("Contrat", "Contrat.html", $"html-templates/{template.Id}", 0, 1, OwnerId);
        document.SetHtmlSource(template.Id, JsonSerializer.Serialize(values), html);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var ai = new CapturingAiService();
        return new Seed(fixture, document, ai,
            new ContractTextProvider(fixture.Db, fixture.GetHandler<IStorageService>(), fixture.GetHandler<IPdfService>()));
    }

    [Fact]
    public async Task The_text_is_the_contract_as_the_signer_sees_it()
    {
        var seed = await SeedAsync("45000", personalData: true);
        using var _ = seed.Fixture;

        var text = await seed.Text.GetTextAsync(seed.Document);

        Assert.Contains("Le Client Smith & Sons paie 45000 TND.", text);     // answers filled in and decoded
        Assert.Contains("Article 2 — Données personnelles", text);            // checkbox clause switched on
        Assert.DoesNotContain("Article 3", text);                              // 45 000 < 100 000: guarantee clause is off
        Assert.DoesNotContain("{{", text);
        Assert.DoesNotContain("<", text);
    }

    [Fact]
    public async Task Conditional_clauses_follow_the_answers()
    {
        var seed = await SeedAsync("250000", personalData: false);
        using var _ = seed.Fixture;

        var text = await seed.Text.GetTextAsync(seed.Document);

        Assert.Contains("Article 3 — Garantie", text);                         // amount over the threshold
        Assert.DoesNotContain("Article 2", text);                              // checkbox off
    }

    [Fact]
    public async Task Harveys_review_reads_an_html_contract_clause_by_clause_and_stores_its_findings()
    {
        var seed = await SeedAsync("45000", personalData: true);
        using var fixture = seed.Fixture;
        fixture.Harvey.Findings = [FakeHarvey.Finding("liability", "High", "Article 1 — Prix")];

        var run = await fixture.GetHandler<StartReviewCommandHandler>().HandleAsync(new StartReviewCommand(seed.Document.Id));
        await new ReviewRunner(fixture.Db, seed.Text, fixture.GetHandler<IContractReviewService>(), fixture.GetHandler<INotificationHub>(),
                fixture.GetHandler<IAuditService>(), Microsoft.Extensions.Options.Options.Create(new ReviewOptions()), NullLogger<ReviewRunner>.Instance)
            .RunAsync(run.Id);

        Assert.Contains(fixture.Harvey.ClausesSent, c => c.Text.Contains("Le Client Smith & Sons paie 45000 TND."));
        var item = await fixture.Db.DocumentReadinessItems.SingleAsync();
        Assert.Equal(("liability", "Article 1 — Prix"), (item.RuleKey, item.ClauseReference));
        Assert.Equal(ReadinessVerdict.NeedsWork, (await fixture.Db.Documents.SingleAsync()).ReadinessVerdict);
    }

    [Fact]
    public async Task The_summary_works_on_an_html_contract_too()
    {
        var seed = await SeedAsync("45000", personalData: true);
        using var fixture = seed.Fixture;

        var summary = await new GetDocumentSummaryQueryHandler(fixture.Db, seed.Text, seed.Ai)
            .HandleAsync(new GetDocumentSummaryQuery(seed.Document.Id));
        Assert.Equal("A short summary.", summary.Summary);
    }
}
