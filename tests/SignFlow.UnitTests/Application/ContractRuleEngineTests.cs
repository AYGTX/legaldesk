#if LEGACY_RULES
// Tests of the keyword rules Harvey's review replaced; compiled only with LEGACY_RULES.
using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>
/// The demo contract, exactly as LegalDesk's PDF reader extracts it: one line per page, no line breaks between
/// articles. Every defect written into it must come out as its own finding, on the right clause, with no AI.
/// </summary>
public class ContractRuleEngineTests
{
    private const string Contract = """
        CONTRAT DE SERVICES Entre Northwind Telecom SARL et Contoso Digital Ltd ENTRE LES PARTIES Le présent Contrat de Services (le Contrat) est conclu entre Northwind Telecom SARL, une société tunisienne (le Prestataire), et Contoso Digital Ltd, une société constituée à l'étranger (le Client). Ailleurs dans ce document, le Prestataire est désigné sous le nom de Northwind Telecom Ltd et le Client sous le nom de Contoso Group. ARTICLE 1 - OBJET Le Prestataire fournira des services d'hébergement géré et de support selon les besoins du Client de temps à autre. La portée des services peut être modifiée par le Client à sa seule discrétion et sans préavis. ARTICLE 2 - PRIX ET PAIEMENT Le Client paiera au Prestataire 120 000 TND hors taxe. Les factures sont payables dans les 30 jours suivant la date de facture. Les factures sont payables dans les 90 jours suivant la réception du rapport de services. Aucun intérêt ni frais de recouvrement ne s'applique en cas de retard de paiement. ARTICLE 3 - DURÉE ET RENOUVELLEMENT TACITE Le présent Contrat entre en vigueur et se renouvellera automatiquement chaque année le 31 décembre 2027 et à la même date de chaque année suivante pour des périodes successives de douze mois, sauf résiliation par le Client. Le Prestataire n'a aucun droit de résiliation. ARTICLE 4 - RESPONSABILITÉ Le Prestataire accepte une responsabilité illimitée pour toute perte, directe ou indirecte, subie par le Client. Le Prestataire indemnisera et dégagera de toute responsabilité le Client contre toutes réclamations, pertes et dépenses de toute nature, sans limite de montant ou de durée. ARTICLE 5 - EXCLUSIVITÉ Pendant la durée du présent Contrat, le Prestataire accorde au Client l'exclusivité sur le marché tunisien de l'hébergement télécom et consent à une obligation de non-concurrence couvrant tous ses autres clients sur ce marché. ARTICLE 6 - PÉNALITÉS Pour tout retard de livraison, le Prestataire paiera au Client une pénalité de 5 pour cent du prix total par jour de retard, à titre de dommages-intérêts forfaitaires, sans plafond. ARTICLE 7 - DONNÉES PERSONNELLES Le Prestataire peut traiter des données personnelles des abonnés du Client dans le cadre des services. Le Prestataire traitera ces données personnelles avec un soin raisonnable. Aucune autre disposition n'est prise à ce sujet. Contrat de Services - Northwind Telecom / Contoso Digital Page 1 sur 2
        ARTICLE 8 - PROPRIÉTÉ INTELLECTUELLE Toute propriété intellectuelle créée ou utilisée par le Prestataire, y compris ses outils préexistants et son savoir-faire, est cédée au Client à compter de la date de signature. ARTICLE 9 - LITIGES Les litiges seront résolus tel qu'indiqué à l'Article 14 du présent Contrat. SIGNATURES Fait en deux (2) exemplaires originaux. Pour le Prestataire : Pour le Client : Northwind Telecom SARL Contoso Digital Ltd Nom / Titre : Nom / Titre : Date & Signature Date & Signature Contrat de Services - Northwind Telecom / Contoso Digital Page 2 sur 2
        """;

    private static readonly IReadOnlyList<SignFlow.Application.Common.Interfaces.RiskFinding> Findings =
        ContractRuleEngine.Analyze(Contract).Findings;

    private static SignFlow.Application.Common.Interfaces.RiskFinding Single(string title) =>
        Assert.Single(Findings, f => f.ClauseType == title);

    [Fact]
    public void Splits_articles_even_when_the_pdf_text_has_no_line_breaks()
    {
        var clauses = ContractRuleEngine.SplitClauses(Contract);
        Assert.Contains(clauses, c => c.StartsWith("ARTICLE 6 - PÉNALITÉS"));
        Assert.Contains(clauses, c => c.StartsWith("ARTICLE 9 - LITIGES") && c.Contains("l'Article 14 du présent Contrat"));
    }

    [Theory]
    [InlineData("Responsabilité illimitée", "High", "Article 4 — Responsabilité", "liability")]
    [InlineData("Garantie sans limite", "High", "Article 4 — Responsabilité", "indemnification")]
    [InlineData("Clause pénale", "High", "Article 6 — Pénalités", "penalty")]
    [InlineData("Résiliation unilatérale", "High", "Article 3 — Durée et renouvellement tacite", "termination")]
    [InlineData("Reconduction tacite", "Medium", "Article 3 — Durée et renouvellement tacite", "auto_renewal")]
    [InlineData("Exclusivité sur tout un marché", "High", "Article 5 — Exclusivité", "exclusivity")]
    [InlineData("Modification unilatérale", "Medium", "Article 1 — Objet", "unilateral_change")]
    [InlineData("Données personnelles", "High", "Article 7 — Données personnelles", "personal_data")]
    [InlineData("Délais de paiement contradictoires", "High", "Article 2 — Prix et paiement", "contradiction")]
    [InlineData("Renvoi à un article inexistant", "High", "Article 9 — Litiges", "contradiction")]
    public void Finds_each_defect_on_its_clause(string title, string severity, string clause, string? ruleKey)
    {
        var finding = Single(title);
        Assert.Equal(severity, finding.Severity);
        Assert.Equal(clause, finding.ContractReference);
        Assert.Equal(ruleKey, finding.RuleKey);
    }

    [Fact]
    public void Explains_with_the_contract_s_own_figures()
    {
        Assert.Contains("30 jours / 90 jours", Single("Délais de paiement contradictoires").Explanation);
        Assert.Contains("5 % du prix par jour", Single("Clause pénale").Explanation);
        Assert.Contains("20 jours", Single("Clause pénale").Explanation);
        Assert.Contains("article 14", Single("Renvoi à un article inexistant").Explanation);
        Assert.Contains("article 9", Single("Renvoi à un article inexistant").Explanation);
        Assert.Contains("étranger", Single("Données personnelles").Explanation);
    }

    [Fact]
    public void Flags_each_party_named_two_ways()
    {
        var parties = Findings.Where(f => f.ClauseType == "Désignation des parties incohérente").ToList();
        Assert.Contains(parties, f => f.Explanation.Contains("Northwind Telecom SARL") && f.Explanation.Contains("Northwind Telecom Ltd"));
        Assert.Contains(parties, f => f.Explanation.Contains("Contoso Digital Ltd") && f.Explanation.Contains("Contoso Group"));
    }

    [Fact]
    public void A_contract_with_termination_does_not_get_a_missing_termination_finding()
    {
        Assert.DoesNotContain(Findings, f => f.ClauseType == "Droit de résiliation");
    }

    [Theory]
    [InlineData("ARTICLE 6 - PÉNALITÉS Le Prestataire paiera une pénalité de 1 % par jour de retard, plafonnée à 10 % du prix total.", "Low")]
    [InlineData("ARTICLE 6 - PÉNALITÉS Le Prestataire paiera une pénalité de 1 % par jour de retard.", "Medium")]
    public void Penalty_severity_follows_what_the_clause_says(string clause, string severity)
    {
        var finding = Assert.Single(ContractRuleEngine.Analyze(clause).Findings, f => f.RuleKey == "penalty");
        Assert.Equal(severity, finding.Severity);
    }

    [Fact]
    public void A_capped_liability_is_not_flagged()
    {
        var findings = ContractRuleEngine.Analyze(
            "ARTICLE 4 - RESPONSABILITÉ La responsabilité illimitée est exclue ; la responsabilité du Prestataire est limitée à 100 % du prix.").Findings;
        Assert.DoesNotContain(findings, f => f.RuleKey == "liability");
    }

    [Fact]
    public void A_reference_to_another_law_is_not_a_missing_article()
    {
        var findings = ContractRuleEngine.Analyze(
            "ARTICLE 1 - OBJET Objet. ARTICLE 2 - PRIX Prix. ARTICLE 3 - DONNÉES Les parties respectent l'article 27 de la loi organique 2004-63.").Findings;
        Assert.DoesNotContain(findings, f => f.ClauseType == "Renvoi à un article inexistant");
    }

    [Fact]
    public void Payment_figures_are_compared_only_within_payment_sentences()
    {
        var findings = ContractRuleEngine.Analyze(
            "ARTICLE 2 - PRIX ET PAIEMENT Les factures sont payables dans les 30 jours. La résiliation exige un préavis de 15 jours.").Findings;
        Assert.DoesNotContain(findings, f => f.ClauseType == "Délais de paiement contradictoires");
    }

    [Fact]
    public void Missing_force_majeure_is_reported_low()
    {
        var finding = Single("Pas de clause de force majeure");
        Assert.Equal("Low", finding.Severity);
        Assert.Equal("force_majeure", finding.RuleKey);
    }

    [Theory]
    [InlineData("ARTICLE 1 - SIGNATURE Le présent contrat est signé par signature électronique.", "electronic_signature", "Medium")]
    [InlineData("ARTICLE 1 - SIGNATURE Le présent contrat est signé par signature électronique certifiée.", "electronic_signature", "Low")]
    [InlineData("ARTICLE 1 - OBJET Le Mandant confie à l'agent commercial la vente de ses produits.", "commercial_agency", "Medium")]
    [InlineData("ARTICLE 1 - VENTE Le consommateur commande en ligne sur le site internet du vendeur.", "consumer_online", "High")]
    [InlineData("ARTICLE 1 - VENTE Le consommateur commande en ligne et dispose d'un droit de rétractation.", "consumer_online", "Low")]
    public void New_risks_are_detected(string clause, string ruleKey, string severity)
    {
        var finding = Assert.Single(ContractRuleEngine.Analyze(clause).Findings, f => f.RuleKey == ruleKey);
        Assert.Equal(severity, finding.Severity);
    }

    [Fact]
    public void Waived_late_payment_interest_is_flagged_on_article_2()
    {
        var finding = Single("Intérêts de retard exclus");
        Assert.Equal("payment", finding.RuleKey);
        Assert.Equal("Article 2 — Prix et paiement", finding.ContractReference);
    }

    [Theory]
    [InlineData("ARTICLE 3 - ARRHES Le Client verse des arrhes de 10 000 TND, définitivement acquises au Prestataire.", "deposit", "Medium")]
    [InlineData("ARTICLE 3 - ACOMPTE Le Client verse un acompte de 30 % à la commande.", "deposit", "Low")]
    [InlineData("ARTICLE 8 - RÉSILIATION En cas de manquement, le Client peut résilier le contrat immédiatement.", "breach_notice", "Medium")]
    [InlineData("ARTICLE 6 - SUSPENSION Le Prestataire peut suspendre les services en cas de retard de paiement.", "suspension", "Low")]
    [InlineData("ARTICLE 1 - OBJET L'Entrepreneur réalise les travaux. ARTICLE 2 - GARANTIE Les travaux sont livrés sans garantie.", "works_guarantee", "High")]
    [InlineData("ARTICLE 1 - OBJET Le transporteur achemine les marchandises. ARTICLE 2 - RESPONSABILITÉ Le transporteur décline toute responsabilité.", "transport", "High")]
    [InlineData("ARTICLE 4 - PAIEMENT Le prix est réglé par paiement électronique.", "electronic_payment", "Low")]
    public void Second_batch_of_risks_is_detected(string text, string ruleKey, string severity)
    {
        var finding = Assert.Single(ContractRuleEngine.Analyze(text).Findings, f => f.RuleKey == ruleKey);
        Assert.Equal(severity, finding.Severity);
    }

    [Fact]
    public void Termination_with_formal_notice_is_not_flagged()
    {
        var findings = ContractRuleEngine.Analyze(
            "ARTICLE 8 - RÉSILIATION En cas de manquement, le Client peut résilier après mise en demeure restée sans effet pendant 15 jours.").Findings;
        Assert.DoesNotContain(findings, f => f.RuleKey == "breach_notice");
    }

    [Fact]
    public void Transport_of_data_is_not_a_transport_contract()
    {
        Assert.DoesNotContain(ContractRuleEngine.Analyze("ARTICLE 1 - SÉCURITÉ Le transport de données est chiffré.").Findings,
            f => f.RuleKey == "transport");
    }

    private const string BankHosting =
        "ARTICLE 1 - OBJET Le Prestataire assure pour la Banque l'hébergement et la maintenance des applications de la Banque, " +
        "dans les conditions et selon les niveaux de service définis par les parties dans le présent contrat. " +
        "ARTICLE 2 - DONNÉES Le Prestataire traite les données des clients de la Banque pour les besoins des services et sur les instructions de la Banque.";

    [Fact]
    public void A_bank_outsourcing_contract_without_supervision_or_audit_is_flagged()
    {
        var findings = ContractRuleEngine.Analyze(BankHosting).Findings;

        var outsourcing = Assert.Single(findings, f => f.RuleKey == "bank_outsourcing");
        Assert.Equal("High", outsourcing.Severity);
        Assert.Contains("Banque centrale", outsourcing.Explanation);
        Assert.Contains("droit d'audit", outsourcing.Explanation);
        Assert.Equal("High", Assert.Single(findings, f => f.RuleKey == "bank_secrecy").Severity);
    }

    [Fact]
    public void A_bank_outsourcing_contract_with_audit_supervision_and_secrecy_is_clean()
    {
        var findings = ContractRuleEngine.Analyze(BankHosting +
            " ARTICLE 3 - AUDIT La Banque et la Banque centrale de Tunisie disposent d'un droit d'audit sur place. " +
            "ARTICLE 4 - SECRET Le Prestataire est tenu au secret professionnel.").Findings;

        Assert.DoesNotContain(findings, f => f.RuleKey is "bank_outsourcing" or "bank_secrecy");
    }

    [Fact]
    public void A_non_bank_contract_gets_no_bank_findings()
    {
        Assert.DoesNotContain(Findings, f => f.RuleKey is "bank_outsourcing" or "bank_secrecy" or "bank_account");
    }

    [Fact]
    public void An_account_agreement_without_fees_or_mediator_is_flagged()
    {
        var finding = Assert.Single(ContractRuleEngine.Analyze(
            "ARTICLE 1 - OUVERTURE DE COMPTE La présente convention de compte régit le compte de dépôt ouvert par la Banque au nom du Client, " +
            "ainsi que les conditions de son fonctionnement et de sa clôture par le Client ou par la Banque dans les cas prévus par la loi.").Findings,
            f => f.RuleKey == "bank_account");
        Assert.Contains("commissions", finding.Explanation);
        Assert.Contains("médiateur", finding.Explanation);
    }
}
#endif
