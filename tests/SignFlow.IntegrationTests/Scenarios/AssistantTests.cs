using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.Assistant;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// The contract assistant (M12): off unless the organisation turned it on, every point backed by a quote found in the
/// contract, every clause read, the participant named when the summary is read for them, and a limit per person.
/// </summary>
public class AssistantTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    /// <summary>Answers one point per clause it is handed, plus one with a quote the contract does not contain.</summary>
    private sealed class ScriptedAi : NullAiService, IAiService
    {
        public List<ClauseText> Sent { get; } = [];

        public new Task<ClauseSummary> SummarizeClausesAsync(IReadOnlyList<ClauseText> clauses, string title, string language, CancellationToken ct = default)
        {
            Sent.AddRange(clauses);
            var points = clauses.Select(c => new SummaryPoint(c.Reference, "Point", c.Text.Trim())).ToList();
            points.Add(new SummaryPoint("Article 9", "Invented", "Le client renonce à tout recours contre le prestataire."));
            return Task.FromResult(new ClauseSummary(points, []));
        }
    }

    private static async Task<(ScenarioFixture Fx, Document Doc, ScriptedAi Ai, ContractSummaries Summaries)> SeedAsync(bool modelOn = true)
    {
        var fx = new ScenarioFixture(OwnerId);
        var doc = Document.Create("Contrat", "c.pdf", "x/c.pdf", 0, 1, OwnerId);
        ((ApplicationDbContext)fx.Db).Documents.Add(doc);
        await fx.Db.SaveChangesAsync();
        await fx.SetModelAsync(modelOn);
        var ai = new ScriptedAi();
        return (fx, doc, ai, new ContractSummaries(fx.Db, fx.Text, ai, fx.GetHandler<ModelAccess>()));
    }

    [Fact]
    public async Task With_the_model_off_the_summary_does_not_exist()
    {
        var (fx, doc, ai, summaries) = await SeedAsync(modelOn: false);
        using var _ = fx;

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => summaries.GetOrCreateAsync(doc, OwnerId, "o@test.com", null, null, default));
        Assert.Equal("model.off", ex.Code);
        Assert.Empty(ai.Sent);
    }

    [Fact]
    public async Task A_point_whose_quote_is_not_in_the_contract_is_dropped_and_the_output_is_labelled()
    {
        var (fx, doc, _, summaries) = await SeedAsync();
        using var _ = fx;

        var summary = await summaries.GetOrCreateAsync(doc, OwnerId, "o@test.com", null, null, default);

        Assert.DoesNotContain(summary.Points, p => p.Point == "Invented");
        Assert.Equal(1, summary.Dropped);
        Assert.NotEmpty(summary.Points);
        Assert.Equal(("groq", "test-model", PromptVersions.Summary), (summary.Label.Provider, summary.Label.Model, summary.Label.PromptVersion));
        Assert.Single(await fx.Db.ModelOutputs.ToListAsync());
    }

    [Fact]
    public async Task Every_clause_is_read_up_to_the_last()
    {
        var (fx, doc, ai, summaries) = await SeedAsync();
        using var _ = fx;
        fx.Text.Text = string.Join("\n\n", Enumerable.Range(1, 60).Select(i =>
            $"ARTICLE {i} - CLAUSE Le prestataire exécute l'obligation numéro {i} dans les délais convenus."));

        var summary = await summaries.GetOrCreateAsync(doc, OwnerId, "o@test.com", null, null, default);

        Assert.Contains(ai.Sent, c => c.Text.Contains("obligation numéro 60 "));
        Assert.Contains(summary.Points, p => p.Quote.Contains("obligation numéro 60 "));
    }

    [Fact]
    public async Task The_stored_summary_is_reused_without_sending_the_text_again()
    {
        var (fx, doc, ai, summaries) = await SeedAsync();
        using var _ = fx;

        await summaries.GetOrCreateAsync(doc, OwnerId, "o@test.com", null, null, default);
        var sent = ai.Sent.Count;
        await summaries.GetOrCreateAsync(doc, OwnerId, "o@test.com", null, null, default);

        Assert.Equal(sent, ai.Sent.Count);
        var audit = (NullAuditService)fx.GetHandler<IAuditService>();
        Assert.Single(audit.Entries, e => e.Action == AuditAction.ModelUsed);
    }

    [Fact]
    public async Task A_participant_sees_the_summary_only_when_shared_and_is_named_in_the_audit()
    {
        var (fx, doc, ai, summaries) = await SeedAsync();
        using var _ = fx;
        var recipient = Recipient.Create(doc.Id, "Leila Ben Ali", "leila@client.tn", 1, ApprovalType.Signature);
        ((ApplicationDbContext)fx.Db).Recipients.Add(recipient);
        await fx.Db.SaveChangesAsync();
        var handler = new GetSigningSummaryQueryHandler(fx.Db, summaries);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(new GetSigningSummaryQuery(recipient.Id)));
        Assert.Equal("summary.notShared", ex.Code);
        Assert.Empty(ai.Sent);

        doc.SetShareSummary(true);
        await fx.Db.SaveChangesAsync();
        await handler.HandleAsync(new GetSigningSummaryQuery(recipient.Id));

        var audit = (NullAuditService)fx.GetHandler<IAuditService>();
        var entry = Assert.Single(audit.Actors, a => a.Action == AuditAction.ModelUsed);
        Assert.Equal((recipient.Id, "Leila Ben Ali", "leila@client.tn"), (entry.RecipientId!.Value, entry.ActorName!, entry.ActorEmail!));
    }

    [Fact]
    public async Task A_person_is_held_to_the_hourly_limit_on_a_contract()
    {
        var (fx, doc, ai, summaries) = await SeedAsync();
        using var _ = fx;
        var db = (ApplicationDbContext)fx.Db;
        for (var i = 0; i < ModelAccess.PerContractPerHour; i++)
            db.AuditLogs.Add(AuditLog.Create(doc.Id, AuditAction.ModelUsed, "system", "earlier call", OwnerId));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<TooManyRequestsException>(() => summaries.GetOrCreateAsync(doc, OwnerId, "o@test.com", null, null, default));
        Assert.Equal("model.limit", ex.Code);
        Assert.Empty(ai.Sent);
    }
}
