#if LEGACY_RULES
// Tests of the keyword rules Harvey's review replaced; compiled only with LEGACY_RULES.
using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>Competition-law risks grounded in loi n° 2015-36 (art. 5 and 36).</summary>
public class CompetitionRiskTests
{
    private const string Distribution = """
        CONTRAT DE DISTRIBUTION Entre la société Alpha, fournisseur, et la société Beta, distributeur, pour la commercialisation des produits du fournisseur en Tunisie. ARTICLE 1 - OBJET Le fournisseur confie au distributeur la revente de ses produits auprès de la clientèle professionnelle. ARTICLE 2 - PRIX DE REVENTE Le distributeur s'engage à ne pas revendre les produits à un prix inférieur au prix public fixé par le fournisseur, et à conserver une marge minimale de quinze pour cent. ARTICLE 3 - DURÉE Le présent contrat est conclu pour une durée de deux ans.
        """;

    private const string Competitors = """
        ACCORD DE COOPÉRATION Entre la société Alpha et la société Gamma, toutes deux actives dans la distribution de matériel informatique en Tunisie. ARTICLE 1 - OBJET Les parties coopèrent pour la logistique de leurs livraisons respectives. ARTICLE 2 - TERRITOIRES Le territoire du Nord est réservé à la société Alpha et celui du Sud à la société Gamma ; chacune des parties s'interdit de démarcher les clients situés sur le territoire de l'autre. ARTICLE 3 - DURÉE Le présent accord est conclu pour une durée de trois ans.
        """;

    private static IReadOnlyList<SignFlow.Application.Common.Interfaces.RiskFinding> Findings(string text) =>
        ContractRuleEngine.Analyze(text).Findings;

    [Fact]
    public void Minimum_resale_price_is_flagged_on_its_clause()
    {
        var finding = Assert.Single(Findings(Distribution), f => f.RuleKey == "resale_price");
        Assert.Equal("Article 2 — Prix de revente", finding.ContractReference);
        Assert.Equal("High", finding.Severity);
        Assert.Contains("nulle de plein droit", finding.Explanation);
    }

    [Fact]
    public void Recommended_resale_price_is_not_flagged() =>
        Assert.DoesNotContain(Findings(Distribution.Replace(
            "s'engage à ne pas revendre les produits à un prix inférieur au prix public fixé par le fournisseur, et à conserver une marge minimale de quinze pour cent",
            "fixe librement ses prix de revente ; le fournisseur peut lui communiquer un prix conseillé")), f => f.RuleKey == "resale_price");

    [Fact]
    public void Territories_shared_between_competitors_are_flagged()
    {
        var finding = Assert.Single(Findings(Competitors), f => f.RuleKey == "market_sharing");
        Assert.Equal("Article 2 — Territoires", finding.ContractReference);
    }

    [Fact]
    public void Plain_cooperation_is_not_flagged() =>
        Assert.DoesNotContain(Findings(Competitors.Replace(
            "Le territoire du Nord est réservé à la société Alpha et celui du Sud à la société Gamma ; chacune des parties s'interdit de démarcher les clients situés sur le territoire de l'autre.",
            "Chaque partie reste libre de commercialiser ses produits sur tout le territoire tunisien.")), f => f.RuleKey == "market_sharing");
}
#endif
