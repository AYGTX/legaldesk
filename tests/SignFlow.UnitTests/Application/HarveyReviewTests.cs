using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.UnitTests.Application;

/// <summary>
/// Harvey finds the risks; LegalDesk groups them one per (rule, clause), attaches the approved law and model clause, and
/// grades them with fixed arithmetic (rework spec R-M8-2 to R-M8-7).
/// </summary>
public class HarveyReviewTests
{
    private static HarveyFinding Finding(string rule, string severity, string clause, string? quote = null, string detection = "CodeCheck",
        double? confidence = null, int version = 1) =>
        new(rule, $"Label {rule}", severity, "x", null, clause, null, version, detection, confidence, quote);

    private static readonly LegalPlaybook Empty = new(new Dictionary<string, IReadOnlyList<PlaybookArticle>>());

    [Fact]
    public void Two_hits_of_a_rule_in_one_clause_are_one_finding_with_both_quotes()
    {
        var grouped = ContractReviewService.Group(
        [
            Finding("liability", "High", "Article 2", "aucun dommage", "MeaningMatch", 0.71),
            Finding("liability", "High", "Article 2", "quelle qu'en soit la cause", "CodeCheck"),
            Finding("liability", "High", "Article 5", "plafond"),
        ], Empty, "fr");

        Assert.Equal(2, grouped.Count);
        var first = grouped[0];
        Assert.Equal(["aucun dommage", "quelle qu'en soit la cause"], first.Quotes);
        Assert.Equal(("CodeCheck", (double?)0.71), (first.DetectionKind, first.Confidence));   // the strongest kind, the best likeness
    }

    [Fact]
    public void Findings_come_most_severe_first_then_in_contract_order()
    {
        var grouped = ContractReviewService.Group(
        [
            Finding("notice", "Low", "Article 1"),
            Finding("penalty", "High", "Article 4"),
            Finding("illicit", "Critical", "Article 6"),
            Finding("liability", "High", "Article 2"),
        ], Empty, "fr");

        Assert.Equal(["illicit", "penalty", "liability", "notice"], grouped.Select(f => f.RuleKey));
    }

    [Fact]
    public void The_approved_law_model_clause_and_approver_come_from_the_grid_in_service()
    {
        var playbook = new LegalPlaybook(
            new Dictionary<string, IReadOnlyList<PlaybookArticle>>
            {
                ["liability"] = [new PlaybookArticle("a1", "Code des obligations et des contrats (Décret du 15 décembre 1906), art. 244",
                    "(Modifié par la loi n° 2005-87) .- Toute clause exonératoire de la faute lourde est nulle.", "Faute lourde",
                    new Dictionary<string, string> { ["fr"] = "On ne peut pas s'exonérer d'une faute grave." })]
            },
            new Dictionary<string, ModelClause> { ["liability"] = new("Clause FR", "Clause EN") },
            new Dictionary<string, RuleApproval> { ["liability"] = new(2, "Me Trabelsi", new DateTime(2026, 10, 30)) });

        var fr = Assert.Single(ContractReviewService.Group([Finding("liability", "High", "Article 2", version: 2)], playbook, "fr"));
        var article = Assert.Single(fr.Articles);
        Assert.Equal("Code des obligations et des contrats, art. 244", article.Citation);
        Assert.Equal("[…] Toute clause exonératoire de la faute lourde est nulle.", article.Quote);
        Assert.Equal("On ne peut pas s'exonérer d'une faute grave.", article.Explanation);
        Assert.Equal(("Clause FR", "Me Trabelsi"), (fr.ProposedLanguage, fr.RuleApprovedBy));

        Assert.Equal("Clause EN", ContractReviewService.Group([Finding("liability", "High", "Article 2", version: 2)], playbook, "en")[0].ProposedLanguage);

        // A finding from an older version is not credited to the approver of the newer one.
        Assert.Null(ContractReviewService.Group([Finding("liability", "High", "Article 2", version: 1)], playbook, "fr")[0].RuleApprovedBy);
    }

    [Fact]
    public void Grading_follows_the_open_findings_and_risk_counts_them_all()
    {
        var items = new[] { Item("Critical"), Item("High"), Item("Medium"), Item("Low") };
        var grade = ReviewGrading.Grade(items);
        Assert.Equal((ReadinessVerdict.Blocked, 61, 39), (grade.Verdict, grade.ReadinessScore, grade.RiskScore));

        items[0].Decide(ReadinessItemStatus.Accepted, Guid.NewGuid(), "Legal accepted it");
        grade = ReviewGrading.Grade(items);
        Assert.Equal((ReadinessVerdict.NeedsWork, 86, 39, 1), (grade.Verdict, grade.ReadinessScore, grade.RiskScore, grade.HandledCount));

        var empty = ReviewGrading.Grade([]);
        Assert.Equal((ReadinessVerdict.Ready, 100, 0), (empty.Verdict, empty.ReadinessScore, empty.RiskScore));
        Assert.Equal(0, ReviewGrading.Grade([Item("Critical"), Item("Critical"), Item("Critical"), Item("Critical"), Item("Critical")]).ReadinessScore);
    }

    [Fact]
    public void A_decision_needs_a_reason_and_fixed_is_not_a_decision()
    {
        var item = Item("High");
        Assert.Throws<InvalidOperationException>(() => item.Decide(ReadinessItemStatus.NotApplicable, Guid.NewGuid(), " "));
        Assert.Throws<InvalidOperationException>(() => item.Decide(ReadinessItemStatus.Fixed, Guid.NewGuid(), "done"));
    }

    [Fact]
    public void Contracts_are_split_into_named_clauses()
    {
        var clauses = ClauseSplitter.ClauseTexts("ARTICLE 1 - OBJET Le contrat a pour objet des services. ARTICLE 2 - DURÉE Le contrat dure un an.");
        Assert.Equal(["Article 1 — Objet", "Article 2 — Durée"], clauses.Select(c => c.Reference));
    }

    [Fact]
    public void Long_articles_are_cut_at_a_sentence_end()
    {
        var text = string.Join(" ", Enumerable.Repeat("Une phrase de l'article assez longue pour compter.", 30));
        var quote = ContractReviewService.Quote(text, 200);
        Assert.EndsWith(". […]", quote);
        Assert.True(quote.Length < 210);
    }

    private static DocumentReadinessItem Item(string severity) =>
        DocumentReadinessItem.Found(Guid.NewGuid(),
            new ReviewFinding($"rule-{Guid.NewGuid():N}", "Article 1", severity, "Label", "x", null, null, null, 1, null, null, null, null, null),
            Guid.NewGuid(), 1);
}
