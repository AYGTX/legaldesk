#if LEGACY_RULES
// Tests of the keyword rules Harvey's review replaced; compiled only with LEGACY_RULES.
using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>
/// The bank demo contract (an outsourcing agreement seeded by <c>DemoContractCatalogue.BankOutsourcingAtlas</c>; keep the two texts in step), as the PDF
/// reader extracts it. Each defect written into it must come out as its own finding with its playbook rule, so the
/// review quotes loi 2016-48 on the bank's own contract.
/// </summary>
public class BankDemoContractTests
{
    internal const string Contract = """
        Contrat d'exploitation — Banque de l'Atlas / Nuage Numérique SARL Réf. LD-2026-BQ-003 ARTICLE 1 - OBJET La Banque confie au Prestataire l'infogérance et l'hébergement de son système bancaire central, y compris la plateforme de banque en ligne et les applications de gestion des comptes de la clientèle. Le Prestataire assure l'exploitation, la maintenance et la sauvegarde de ces systèmes. ARTICLE 2 - DURÉE ET RECONDUCTION Le présent Contrat est conclu pour une durée initiale de trois (3) ans. Il se renouvelle ensuite par reconduction tacite pour des périodes successives de trois (3) ans, sauf dénonciation notifiée au moins six (6) mois avant l'échéance. ARTICLE 3 - PRIX Les prestations sont rémunérées selon le barème annexé. Le Prestataire peut modifier ses tarifs et le périmètre des prestations à sa seule discrétion et sans préavis. ARTICLE 4 - DONNÉES DES CLIENTS Le Prestataire héberge les fichiers et les données personnelles des clients de la Banque sur ses serveurs situés en France et peut les communiquer à ses propres sous-traitants établis à l'étranger. Le Prestataire traite ces informations avec un soin raisonnable. ARTICLE 5 - RESPONSABILITÉ La Banque indemnisera et garantira le Prestataire contre toutes réclamations, pertes et dépenses de toute nature liées aux prestations, sans limite de montant ou de durée. ARTICLE 6 - RÉSILIATION Le Prestataire peut résilier le présent Contrat à tout moment, sans préavis ni indemnité. La Banque n'a aucun droit de résiliation. ARTICLE 7 - LITIGES Les litiges seront résolus tel qu'indiqué à l'Article 12 du présent Contrat. Fait à Tunis, en deux (2) exemplaires originaux.
        """;

    private static readonly IReadOnlyList<SignFlow.Application.Common.Interfaces.RiskFinding> Findings =
        ContractRuleEngine.Analyze(Contract).Findings;

    [Theory]
    [InlineData("bank_outsourcing", "Article 1 — Objet")]
    [InlineData("bank_secrecy", "Article 4 — Données des clients")]
    [InlineData("personal_data", "Article 4 — Données des clients")]
    [InlineData("auto_renewal", "Article 2 — Durée et reconduction")]
    [InlineData("unilateral_change", "Article 3 — Prix")]
    [InlineData("indemnification", "Article 5 — Responsabilité")]
    [InlineData("termination", "Article 6 — Résiliation")]
    [InlineData("contradiction", "Article 7 — Litiges")]
    public void Each_defect_is_found_on_its_clause(string ruleKey, string clause) =>
        Assert.Contains(Findings, f => f.RuleKey == ruleKey && f.ContractReference == clause);

    [Fact]
    public void The_indemnity_finding_names_the_bank_as_the_party_at_risk() =>
        Assert.Contains("Ici, la Banque garantit le Prestataire : le risque pèse sur la Banque.",
            Assert.Single(Findings, f => f.RuleKey == "indemnification").Explanation);

    [Theory]
    [InlineData("ARTICLE 1 - GARANTIE Le Prestataire indemnisera et dégagera de toute responsabilité le Client contre toute réclamation des tiers liée à l'exécution des prestations, dans les conditions prévues par le présent contrat et pour toute la durée de celui-ci.", "Ici, le Prestataire garantit le Client")]
    [InlineData("ARTICLE 1 - INDEMNITY The Supplier shall indemnify and hold harmless the Customer against any claim of any kind.", "Here the Supplier indemnifies the Customer")]
    public void Names_the_indemnifying_party(string clause, string expected) =>
        Assert.Contains(expected, Assert.Single(ContractRuleEngine.Analyze(clause).Findings, f => f.RuleKey == "indemnification").Explanation);

    [Fact]
    public void Says_nothing_about_parties_when_the_wording_does_not_name_them() =>
        Assert.DoesNotContain("Ici,", Assert.Single(
            ContractRuleEngine.Analyze("ARTICLE 1 - GARANTIE Une indemnisation de toute nature est due au Client en cas de sinistre survenu pendant la durée du présent contrat et pour toutes les prestations qui y sont décrites.").Findings,
            f => f.RuleKey == "indemnification").Explanation);
}
#endif
