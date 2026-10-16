using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

public class LegalPlaybookServiceTests
{
    private static readonly PlaybookArticle Coc244 = new("a-244",
        "Code des obligations et des contrats (Décret du 15 décembre 1906), art. 244",
        "On ne peut stipuler d'avance qu'on ne sera pas tenu de sa faute lourde ou de son dol.",
        "Limite légale des clauses sur la responsabilité.");

    private static readonly PlaybookArticle Dp27 = new("a-27", "Loi organique n° 2004-63 du 27 juillet 2004, art. 27",
        "Le traitement des données à caractère personnel ne peut être effectué qu'avec le consentement exprès et écrit.", null);

    private static readonly LegalPlaybook Playbook = new(
        new Dictionary<string, IReadOnlyList<PlaybookArticle>> { ["liability"] = [Coc244], ["personal_data"] = [Dp27] },
        new Dictionary<string, string> { ["2004-63"] = "personal_data" });

    [Fact]
    public void A_rule_finding_quotes_its_articles_word_for_word()
    {
        var risk = new ContractRiskAnalysis(40,
            [new RiskFinding("Responsabilité", "High", "Responsabilité illimitée.", RuleKey: "liability"),
             new RiskFinding("Autre", "Low", "x", RuleKey: "unknown")],
            DateTime.UtcNow);

        var result = LegalPlaybookService.AttachToRisk(risk, Playbook);

        Assert.Equal(
            "Code des obligations et des contrats, art. 244 : « On ne peut stipuler d'avance qu'on ne sera pas tenu de sa faute lourde ou de son dol. »\n→ Limite légale des clauses sur la responsabilité.",
            result.Findings[0].WhyItMatters);
        Assert.Null(result.Findings[1].WhyItMatters);
    }

    [Fact]
    public void A_model_finding_keeps_its_own_reasoning()
    {
        var risk = new ContractRiskAnalysis(40, [new RiskFinding("Liability", "High", "x", WhyItMatters: "Model reasoning", RuleKey: "liability")], DateTime.UtcNow);

        Assert.Equal("Model reasoning", LegalPlaybookService.AttachToRisk(risk, Playbook).Findings[0].WhyItMatters);
    }

    [Fact]
    public void A_regulation_gets_its_mapped_article_as_a_verified_legal_basis()
    {
        var dataLaw = new RegulatoryTextSummary(Guid.NewGuid(), "Loi sur la protection des données", "données", "Loi organique n° 2004-63 du 27 juillet 2004");
        var consumer = new RegulatoryTextSummary(Guid.NewGuid(), "Code de la consommation", "consommateur", null);
        var compliance = new ComplianceAnalysis(50,
            [new ComplianceFinding(dataLaw.Id, dataLaw.Title, "Gap", "x"), new ComplianceFinding(consumer.Id, consumer.Title, "Gap", "y")],
            DateTime.UtcNow);

        var result = LegalPlaybookService.AttachToCompliance(compliance, [dataLaw, consumer], Playbook);

        Assert.Equal("a-27", result.Findings[0].LegalArticleId);
        Assert.Equal(LegalCitationGuard.Verified, result.Findings[0].CitationStatus);
        Assert.Equal(Dp27.Text, result.Findings[0].LegalQuote);
        Assert.Null(result.Findings[1].LegalArticleId);
    }

    [Fact]
    public void An_amendment_note_is_left_out_of_the_excerpt()
    {
        var quote = LegalPlaybookService.Quote("(Modifié par la loi n°59-148 du 7 novembre 1959) .- Les dommages sont la perte effective.");

        Assert.Equal("[…] Les dommages sont la perte effective.", quote);
    }

    [Fact]
    public void A_long_article_is_cut_at_a_sentence_end_and_marked()
    {
        var text = string.Join(" ", Enumerable.Repeat("Une phrase complète de l'article qui continue encore un peu.", 20));

        var quote = LegalPlaybookService.Quote(text);

        Assert.EndsWith(". […]", quote);
        Assert.True(quote.Length <= 710);
        Assert.StartsWith(quote[..^6], text);
    }
}
