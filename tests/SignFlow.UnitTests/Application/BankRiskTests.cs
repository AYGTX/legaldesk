#if LEGACY_RULES
// Tests of the keyword rules Harvey's review replaced; compiled only with LEGACY_RULES.
using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>The bank risks grounded in loi 2016-48 (art. 62, 65, 82) and loi organique 2015-26 (art. 108, 113).</summary>
public class BankRiskTests
{
    private const string Risky = """
        CONTRAT DE PRESTATION Entre la Banque de l'Atlas et Atlas Services SARL, société du groupe de la Banque. ARTICLE 1 - OBJET La Banque confie au Prestataire, société du groupe, l'identification des clients lors de l'entrée en relation, la collecte des pièces d'identité et l'ouverture des dossiers. ARTICLE 2 - CONFIDENTIALITÉ Le Prestataire s'engage à ne divulguer à aucun tiers les informations reçues de la Banque, sans exception, pendant toute la durée du contrat et après son terme. ARTICLE 3 - CONSERVATION Les dossiers des clients sont conservés par le Prestataire pendant cinq (5) ans puis détruits. ARTICLE 4 - PRIX Les prestations sont facturées selon le barème annexé au présent contrat, payables à trente jours.
        """;

    private const string WellDrafted = """
        CONTRAT DE PRESTATION Entre la Banque de l'Atlas et Atlas Services SARL, société du groupe de la Banque. ARTICLE 1 - OBJET La Banque confie au Prestataire, société du groupe, l'identification des clients lors de l'entrée en relation. Le Prestataire est soumis à la réglementation relative à la lutte contre le blanchiment d'argent et le financement du terrorisme et remet sans délai à la Banque les données d'identification. ARTICLE 2 - AUTORISATION La présente convention a été autorisée par le conseil d'administration de la Banque et sera soumise à l'assemblée générale. ARTICLE 3 - CONFIDENTIALITÉ Le Prestataire s'engage à ne divulguer à aucun tiers les informations reçues, sauf à la Banque centrale de Tunisie et aux autorités de contrôle. ARTICLE 4 - CONSERVATION Les dossiers des clients sont conservés pendant dix (10) ans après la clôture du compte.
        """;

    private static IReadOnlyList<SignFlow.Application.Common.Interfaces.RiskFinding> Findings(string text) =>
        ContractRuleEngine.Analyze(text).Findings;

    [Theory]
    [InlineData("bank_related_party", "Convention réglementée")]
    [InlineData("bank_supervisor_access", "Confidentialité sans exception pour la BCT")]
    [InlineData("bank_aml", "Identification des clients confiée à un tiers")]
    [InlineData("bank_aml", "Conservation des documents inférieure à dix ans")]
    public void Risky_bank_contract_raises_each_risk(string ruleKey, string title) =>
        Assert.Contains(Findings(Risky), f => f.RuleKey == ruleKey && f.ClauseType == title);

    [Fact]
    public void Each_finding_sits_on_its_clause()
    {
        var findings = Findings(Risky);
        Assert.Equal("Article 2 — Confidentialité", findings.Single(f => f.RuleKey == "bank_supervisor_access").ContractReference);
        Assert.Equal("Article 3 — Conservation", findings.Single(f => f.ClauseType.StartsWith("Conservation")).ContractReference);
    }

    [Fact]
    public void Well_drafted_contract_raises_none()
    {
        var keys = Findings(WellDrafted).Select(f => f.RuleKey).ToList();
        Assert.DoesNotContain("bank_related_party", keys);
        Assert.DoesNotContain("bank_supervisor_access", keys);
        Assert.DoesNotContain("bank_aml", keys);
    }

    [Fact]
    public void Non_bank_contract_is_left_alone() =>
        Assert.DoesNotContain(Findings(Risky.Replace("Banque", "Société")),
            f => f.RuleKey is "bank_related_party" or "bank_supervisor_access" or "bank_aml");

    [Fact]
    public void Account_agreement_without_complaint_procedure()
    {
        const string text = "CONVENTION DE COMPTE ARTICLE 1 - OUVERTURE La banque ouvre au client un compte de dépôt à vue. ARTICLE 2 - TARIFS Les commissions et frais figurent au barème remis au client lors de l'ouverture. ARTICLE 3 - MÉDIATION Le client peut saisir le médiateur bancaire en cas de différend avec la banque concernant le fonctionnement du compte.";
        Assert.Contains(Findings(text), f => f.RuleKey == "bank_customer_complaints");
        Assert.DoesNotContain(Findings(text + " ARTICLE 4 - RÉCLAMATIONS Toute réclamation est traitée dans un délai de quinze jours."),
            f => f.RuleKey == "bank_customer_complaints");
    }
}
#endif
