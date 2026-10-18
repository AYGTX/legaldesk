using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>Wording a lawyer adds to a playbook rule raises that rule on clauses the built-in detection misses.</summary>
public class PlaybookWordingTests
{
    private const string Contract = """
        CONTRAT DE PRESTATION Entre la société Alpha et la société Beta, pour la fourniture de services informatiques à la clientèle du Client. ARTICLE 1 - OBJET Le Prestataire fournit au Client des services de maintenance de son parc informatique pendant toute la durée du présent contrat. ARTICLE 2 - CONSÉQUENCES Le Client prendra en charge l'intégralité des conséquences de tout incident survenu dans ses locaux, quelle qu'en soit la cause, pendant toute la durée du présent contrat.
        """;

    private static readonly Dictionary<string, PlaybookWording> Wording = new()
    {
        ["indemnification"] = new("Indemnisation", ["prendra en  CHARGE l’integralite des consequences"])
    };

    [Fact]
    public void Built_in_rules_miss_the_wording() =>
        Assert.DoesNotContain(ContractRuleEngine.Analyze(Contract).Findings, f => f.RuleKey == "indemnification");

    [Fact]
    public void Lawyer_phrase_raises_the_rule_on_its_clause_ignoring_case_accents_and_spacing()
    {
        var risk = ContractRuleEngine.ApplyPlaybookWording(ContractRuleEngine.Analyze(Contract), Contract, Wording);

        var finding = Assert.Single(risk.Findings, f => f.RuleKey == "indemnification");
        Assert.Equal("Article 2 — Conséquences", finding.ContractReference);
        Assert.Equal("Indemnisation", finding.ClauseType);
        Assert.Equal(ContractRuleEngine.Medium, finding.Severity);
        Assert.Contains("La grille des risques signale la formulation", finding.Explanation);
        Assert.DoesNotContain(risk.Findings, f => f.ClauseType == "Général");
    }

    [Fact]
    public void Does_not_duplicate_a_finding_the_built_in_rule_already_raised()
    {
        const string text = "ARTICLE 1 - GARANTIE Le Prestataire indemnisera le Client contre toute réclamation de toute nature liée aux prestations décrites dans le présent contrat et pendant toute sa durée.";
        var wording = new Dictionary<string, PlaybookWording> { ["indemnification"] = new("Indemnisation", ["indemnisera le client"]) };

        var risk = ContractRuleEngine.ApplyPlaybookWording(ContractRuleEngine.Analyze(text), text, wording);

        Assert.Single(risk.Findings, f => f.RuleKey == "indemnification");
    }

    [Fact]
    public void Ignores_phrases_too_short_to_mean_anything()
    {
        var wording = new Dictionary<string, PlaybookWording> { ["indemnification"] = new("Indemnisation", ["le"]) };
        var before = ContractRuleEngine.Analyze(Contract);
        Assert.Same(before, ContractRuleEngine.ApplyPlaybookWording(before, Contract, wording));
    }
}
