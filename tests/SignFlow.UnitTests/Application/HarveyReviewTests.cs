using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>Harvey reviews contracts; LegalDesk orders and scores its findings, and says so when Harvey is down.</summary>
public class HarveyReviewTests
{
    [Fact]
    public void Findings_are_ordered_by_severity_and_scored()
    {
        var analysis = ContractRuleEngine.FromFindings(
        [
            new RiskFinding("Force majeure", ContractRuleEngine.Low, "x", RuleKey: "force_majeure"),
            new RiskFinding("Responsabilité illimitée", ContractRuleEngine.High, "y", RuleKey: "liability"),
            new RiskFinding("Lead", ContractRuleEngine.Low, "z", FindingType: ContractRuleEngine.Possible),
        ], "fr");

        Assert.Equal("liability", analysis.Findings[0].RuleKey);
        Assert.Equal(22, analysis.RiskScore); // 20 + 2; a possible lead is not scored
    }

    [Fact]
    public void No_finding_says_harvey_found_nothing() =>
        Assert.Contains("Harvey", Assert.Single(ContractRuleEngine.FromFindings([], "fr").Findings).Explanation);

    [Fact]
    public void Harvey_down_is_a_limitation_not_an_empty_review()
    {
        var analysis = ContractRuleEngine.ReviewUnavailable("fr");
        Assert.Empty(analysis.Findings);
        Assert.Contains("indisponible", Assert.Single(analysis.Limitations!));
    }

    [Fact]
    public void Contracts_are_split_into_named_clauses()
    {
        var clauses = ContractRuleEngine.ClauseTexts("ARTICLE 1 - OBJET Le contrat a pour objet des services. ARTICLE 2 - DURÉE Le contrat dure un an.");
        Assert.Equal(["Article 1 — Objet", "Article 2 — Durée"], clauses.Select(c => c.Reference));
    }
}
