#if LEGACY_RULES
// Tests of the keyword rules Harvey's review replaced; compiled only with LEGACY_RULES.
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

public class PlaybookExplanationTests
{
    private static readonly PlaybookArticle Article = new("a1", "Code des obligations et des contrats (Décret du 15 décembre 1906), art. 278",
        "Les dommages sont la perte effective que le créancier a éprouvée.", "Ce que couvrent les dommages-intérêts.",
        new Dictionary<string, string> { ["fr"] = "Les dommages couvrent la perte réelle.", ["en"] = "Damages cover the actual loss." });

    [Fact]
    public void Approved_explanation_follows_the_quote_in_the_contract_language()
    {
        var fr = LegalPlaybookService.Format([Article], "fr");
        Assert.EndsWith("En termes simples : Les dommages couvrent la perte réelle.", fr);
        Assert.True(fr.IndexOf("« Les dommages sont", StringComparison.Ordinal) < fr.IndexOf("En termes simples", StringComparison.Ordinal));
        Assert.EndsWith("In plain words: Damages cover the actual loss.", LegalPlaybookService.Format([Article], "en"));
    }

    [Fact]
    public void Without_an_approved_explanation_nothing_is_added() =>
        Assert.DoesNotContain("En termes simples", LegalPlaybookService.Format([Article with { Explanations = null }], "fr"));
}

public class ModelClauseTests
{
    private static readonly LegalPlaybook Playbook = new(
        new Dictionary<string, IReadOnlyList<PlaybookArticle>>(), new Dictionary<string, string>(),
        ModelClauses: new Dictionary<string, ModelClause>
        {
            ["liability"] = new("La responsabilité de chaque partie est limitée au prix annuel.", "Each party's liability is capped at the annual price."),
            ["penalty"] = new("Les pénalités sont plafonnées à dix pour cent du prix.", null)
        });

    private static RiskFinding Finding(string key) => new("Titre", "High", "Explication", RuleKey: key);

    [Fact]
    public void Approved_clause_becomes_the_proposed_wording_in_the_contract_language()
    {
        Assert.Equal("La responsabilité de chaque partie est limitée au prix annuel.",
            LegalPlaybookService.WithModelClause(Finding("liability"), Playbook, "fr").ProposedLanguage);
        Assert.Equal("Each party's liability is capped at the annual price.",
            LegalPlaybookService.WithModelClause(Finding("liability"), Playbook, "en").ProposedLanguage);
        Assert.Equal("Les pénalités sont plafonnées à dix pour cent du prix.",
            LegalPlaybookService.WithModelClause(Finding("penalty"), Playbook, "en").ProposedLanguage);
    }

    [Fact]
    public void Existing_proposals_and_risks_without_a_clause_are_left_alone()
    {
        var withOwn = Finding("liability") with { ProposedLanguage = "Proposition du modèle." };
        Assert.Equal("Proposition du modèle.", LegalPlaybookService.WithModelClause(withOwn, Playbook, "fr").ProposedLanguage);
        Assert.Null(LegalPlaybookService.WithModelClause(Finding("exclusivity"), Playbook, "fr").ProposedLanguage);
    }
}
#endif
