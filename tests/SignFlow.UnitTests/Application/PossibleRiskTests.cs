using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>Meaning-based matches from the corpus service become "possible risk" leads, apart from certain findings.</summary>
public class PossibleRiskTests
{
    private const string Contract = """
        CONTRAT DE PRESTATION Entre la société Alpha et la société Beta, pour la fourniture de services informatiques à la clientèle du Client. ARTICLE 1 - OBJET Le Prestataire fournit au Client des services de maintenance de son parc informatique pendant toute la durée du présent contrat. ARTICLE 2 - CONSÉQUENCES La Banque assumera la totalité des pertes subies par le Prestataire, sans aucune restriction, pendant toute la durée du présent contrat.
        """;

    private static ClauseMatch Match(string reference, string key = "indemnification") =>
        new(reference, key, "Indemnisation", 0.71, "La Banque assumera la totalité des pertes subies par le Prestataire, sans aucune restriction.",
            "Le Client supportera seul l'ensemble des conséquences financières de tout sinistre.");

    [Fact]
    public void Clause_texts_carry_the_review_names()
    {
        var clauses = ContractRuleEngine.ClauseTexts(Contract);
        Assert.Contains(clauses, c => c.Reference == "Article 2 — Conséquences" && c.Text.Contains("totalité des pertes"));
    }

    [Fact]
    public void A_match_becomes_a_possible_finding_left_out_of_the_score()
    {
        var before = ContractRuleEngine.Analyze(Contract);
        var after = ContractRuleEngine.ApplyPossibleMatches(before, Contract, [Match("Article 2 — Conséquences")]);

        var finding = Assert.Single(after.Findings, f => f.FindingType == ContractRuleEngine.Possible);
        Assert.Equal("Risque possible : Indemnisation", finding.ClauseType);
        Assert.Equal("indemnification", finding.RuleKey);
        Assert.Equal("Article 2 — Conséquences", finding.ContractReference);
        Assert.Contains("similarité 0,71", finding.Explanation);
        Assert.Contains("Exemple le plus proche", finding.RecommendedAction);
        Assert.Equal(after.Findings.Where(f => f.FindingType != ContractRuleEngine.Possible)
            .Sum(f => f.Severity switch { "High" => 20, "Medium" => 10, _ => 2 }), after.RiskScore);
    }

    [Fact]
    public void No_lead_where_a_certain_finding_already_raised_the_rule()
    {
        var risk = ContractRuleEngine.Analyze(BankDemoContractTests.Contract);
        var after = ContractRuleEngine.ApplyPossibleMatches(risk, BankDemoContractTests.Contract, [Match("Article 5 — Responsabilité")]);
        Assert.DoesNotContain(after.Findings, f => f.FindingType == ContractRuleEngine.Possible);
    }
}
