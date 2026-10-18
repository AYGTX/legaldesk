using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>Refinements prompted by the CUAD benchmark (tools/cuad-benchmark): wording written for tests, not CUAD's.</summary>
public class RuleRefinementTests
{
    private static IReadOnlyList<string?> Keys(string text) =>
        ContractRuleEngine.Analyze(text).Findings.Select(f => f.RuleKey).ToList();

    private const string Header = "SERVICES AGREEMENT between Alpha Corp and Beta Ltd for the provision of software services to the Customer. ";

    [Theory]
    [InlineData("ARTICLE 1 - OWNERSHIP All deliverables remain the sole and exclusive property of the Supplier. ARTICLE 2 - LAW The courts of Tunis have exclusive jurisdiction over any dispute arising from this Agreement.")]
    [InlineData("ARTICLE 1 - LICENSE The Supplier grants the Customer a non-exclusive, non-transferable license to use the software. ARTICLE 2 - REMEDY Replacement is the Customer's sole and exclusive remedy for defects.")]
    [InlineData("ARTICLE 1 - PROPRIÉTÉ Les livrables restent la propriété exclusive du Prestataire, qui concède au Client une licence à titre non exclusif. ARTICLE 2 - LITIGES Les tribunaux de Tunis ont compétence exclusive pour connaître de tout litige.")]
    public void Benign_uses_of_exclusive_are_not_exclusivity(string body) =>
        Assert.DoesNotContain("exclusivity", Keys(Header + body));

    [Theory]
    [InlineData("ARTICLE 1 - EXCLUSIVITY During the term, the Customer shall purchase all its requirements of such services exclusively from the Supplier and shall not engage any other supplier.")]
    [InlineData("ARTICLE 1 - NON-COMPETE The Supplier agrees not to compete with the Customer in the Tunisian market for two years after termination.")]
    public void Real_exclusivity_is_still_found(string body) =>
        Assert.Contains("exclusivity", Keys(Header + body));

    [Fact]
    public void Cap_with_broad_carve_outs_is_flagged()
    {
        var findings = ContractRuleEngine.Analyze(Header + "ARTICLE 1 - LIABILITY Each party's aggregate liability shall not exceed the fees paid in the prior twelve months. The liability cap shall not apply to indemnification obligations or to any breach of confidentiality.").Findings;
        var finding = Assert.Single(findings, f => f.RuleKey == "liability");
        Assert.Equal("Liability cap with broad exceptions", finding.ClauseType);
        Assert.Equal("Medium", finding.Severity);
    }

    [Theory]
    [InlineData("ARTICLE 1 - LIABILITY Except in the event of fraud or gross negligence, each party's aggregate liability shall not exceed the fees paid in the prior twelve months.")]
    [InlineData("ARTICLE 1 - RESPONSABILITÉ Sauf en cas de faute lourde ou de dol, la responsabilité globale de chaque partie ne saurait excéder le montant des sommes versées au cours des douze derniers mois.")]
    public void Standard_fraud_and_gross_negligence_exceptions_are_not_flagged(string body) =>
        Assert.DoesNotContain("liability", Keys(Header + body));

    [Theory]
    [InlineData("ARTICLE 1 - TERM This Agreement has an initial term of two years and shall renew automatically for successive periods of one year.")]
    [InlineData("ARTICLE 1 - TERM Thereafter, this Agreement shall be automatically extended for additional periods of one year unless either party gives written notice of its intention not to renew at least 90 days before expiry.")]
    public void English_renewal_wording_is_found(string body) =>
        Assert.Contains("auto_renewal", Keys(Header + body));

    [Fact]
    public void A_fixed_term_is_not_a_renewal() =>
        Assert.DoesNotContain("auto_renewal", Keys(Header + "ARTICLE 1 - TERM This Agreement has a term of two years and expires at the end of that term without renewal."));
}
