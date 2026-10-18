using System.Globalization;
using System.Text.RegularExpressions;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// Deterministic contract review, clause by clause, in French and English. Each rule reads the clause it fires on, so
/// severity follows what the clause actually says ("pénalité sans plafond" is not "pénalité plafonnée à 10 %"), and
/// structural checks catch what a word scan cannot: contradictory figures, references to missing articles, parties
/// named inconsistently, personal data handed to a foreign party. Every finding names the clause it comes from and
/// carries the playbook key that links it to verified law.
/// </summary>
public static class ContractRuleEngine
{
    public const string High = "High";
    public const string Medium = "Medium";
    public const string Low = "Low";

    private sealed record Clause(string Heading, string Text, int? Number);

    private sealed class Lang(bool fr)
    {
        public string T(string en, string french) => fr ? french : en;
    }

    public static ContractRiskAnalysis Analyze(string? text)
    {
        text ??= string.Empty;
        var l = new Lang(ContractLanguage.Detect(text) == "fr");
        var clauses = SplitClauses(text).Select(ToClause).ToList();
        var findings = new List<RiskFinding>();

        Liability(clauses, l, findings);
        Indemnification(clauses, l, findings);
        Penalty(clauses, l, findings);
        Termination(text, clauses, l, findings);
        Renewal(clauses, l, findings);
        Exclusivity(clauses, l, findings);
        UnilateralChange(clauses, l, findings);
        PersonalData(text, clauses, l, findings);
        ContradictoryPaymentTerms(clauses, l, findings);
        MissingReferences(clauses, l, findings);
        InconsistentParties(text, l, findings);
        ForceMajeure(text, clauses, l, findings);
        ElectronicSignature(clauses, l, findings);
        CommercialAgency(clauses, l, findings);
        ConsumerOnlineSale(text, clauses, l, findings);
        LateInterestWaived(clauses, l, findings);
        Deposit(clauses, l, findings);
        BreachWithoutNotice(clauses, l, findings);
        Suspension(clauses, l, findings);
        WorksGuarantee(clauses, l, findings);
        Transport(clauses, l, findings);
        ElectronicPayment(clauses, l, findings);
        BankOutsourcing(text, clauses, l, findings);
        BankSecrecy(text, clauses, l, findings);
        BankAccountAgreement(text, clauses, l, findings);

        if (findings.Count == 0)
            findings.Add(new RiskFinding(l.T("General", "Général"), Low,
                l.T("No risk pattern detected in an automated review.", "Aucun point de risque détecté par l'analyse automatique.")));

        var ordered = findings.OrderBy(f => f.Severity switch { High => 0, Medium => 1, _ => 2 }).ToList();
        var score = Math.Min(100, ordered.Sum(f => f.Severity switch { High => 20, Medium => 10, _ => 2 }));
        return new ContractRiskAnalysis(score, ordered, DateTime.UtcNow);
    }

    /// <summary>
    /// Flags each clause that contains wording a lawyer attached to a confirmed playbook rule, unless a built-in rule
    /// already raised that risk on the same clause. Matching ignores case, accents and spacing; the finding names the
    /// phrase it found, so the reviewer sees exactly why it was raised.
    /// </summary>
    public static ContractRiskAnalysis ApplyPlaybookWording(
        ContractRiskAnalysis risk, string? text, IReadOnlyDictionary<string, PlaybookWording> wording)
    {
        if (string.IsNullOrWhiteSpace(text) || wording.Count == 0) return risk;
        var l = new Lang(ContractLanguage.Detect(text) == "fr");
        var clauses = SplitClauses(text).Select(ToClause).ToList();
        var findings = risk.Findings.ToList();
        var added = 0;

        foreach (var (key, rule) in wording)
        foreach (var c in clauses)
        {
            if (findings.Any(f => f.RuleKey == key && f.ContractReference == c.Heading)) continue;
            var body = Fold(c.Text);
            var phrase = rule.Phrases.FirstOrDefault(p => Fold(p).Length >= 4 && body.Contains(Fold(p), StringComparison.Ordinal));
            if (phrase is null) continue;

            findings.Add(Finding(c, key, rule.Label, Medium,
                l.T($"The playbook flags the wording “{phrase.Trim()}” for this risk.",
                    $"Le playbook signale la formulation « {phrase.Trim()} » pour ce risque."),
                l.T($"Check this clause against the “{rule.Label}” risk.",
                    $"Vérifier cette clause au regard du risque « {rule.Label} ».")));
            added++;
        }
        if (added == 0) return risk;

        // The "nothing detected" placeholder no longer holds once a playbook phrase has matched.
        findings.RemoveAll(f => f.RuleKey is null && f.Severity == Low && f.ContractReference is null
                                && (f.ClauseType == "General" || f.ClauseType == "Général"));
        var ordered = findings.OrderBy(f => f.Severity switch { High => 0, Medium => 1, _ => 2 }).ToList();
        var score = Math.Min(100, ordered.Sum(f => f.Severity switch { High => 20, Medium => 10, _ => 2 }));
        return risk with { Findings = ordered, RiskScore = score };
    }

    /// <summary>Lower case, no accents, one space between words, straight apostrophes: "Prendra  en CHARGE l’intégralité" → "prendra en charge l'integralite".</summary>
    private static string Fold(string s)
    {
        var decomposed = s.Replace('’', '\'').Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    // ------------------------------------------------------------------ clause rules

    private static void Liability(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "responsabilité illimitée", "unlimited liability", "responsabilité sans limite")))
        {
            if (Has(c, "limitée à", "plafonnée", "limited to", "capped at")) continue;
            findings.Add(Finding(c, "liability", l.T("Unlimited liability", "Responsabilité illimitée"), High,
                l.T("The clause accepts liability with no cap, including indirect losses where stated.",
                    "La clause accepte une responsabilité sans aucun plafond" + (Has(c, "indirecte", "indirect") ? ", y compris pour les pertes indirectes." : ".")),
                l.T("Cap liability (e.g. at the contract price) and exclude indirect losses, except for gross negligence or fraud.",
                    "Plafonner la responsabilité (par exemple au prix du contrat) et exclure les pertes indirectes, sauf faute lourde ou dol.")));
        }
    }

    // "La Banque indemnisera et garantira le Prestataire", "The Supplier shall indemnify and hold harmless the Customer".
    // Party names must be capitalised, so "la garantie" or "le présent contrat" is never read as a party.
    private static readonly Regex IndemnityFr = new(
        @"\b(?<g>(?i:le|la|l['’])\s*[A-ZÉ][\p{L}-]+)\s+(?:s['’]engage\s+à\s+)?(?:indemnisera|indemnise|indemniser|garantira|garantit|garantir|tiendra\s+indemne|dégagera)\b"
        + @"(?:\s+et\s+(?:garantira|garantir|indemnisera|indemniser|tiendra\s+indemne|dégagera(?:\s+de\s+toute\s+responsabilité)?))?\s+(?<b>(?i:le|la|l['’])\s*[A-ZÉ][\p{L}-]+)",
        RegexOptions.Compiled);
    private static readonly Regex IndemnityEn = new(
        @"\b(?<g>[Tt]he\s+[A-Z][\w-]+)\s+(?i:shall|will|agrees\s+to)\s+(?i:indemnify|hold\s+harmless)(?:\s+(?i:and|,)\s+(?i:defend|hold\s+harmless|indemnify))*\s+(?<b>the\s+[A-Z][\w-]+)",
        RegexOptions.Compiled);

    /// <summary>Who gives the indemnity and who benefits, as written ("la Banque", "le Prestataire"); null when unclear.</summary>
    private static (string Giver, string Beneficiary)? IndemnityParties(string text)
    {
        var m = IndemnityFr.Match(text);
        if (!m.Success) m = IndemnityEn.Match(text);
        if (!m.Success) return null;
        static string Party(string s) => char.ToLowerInvariant(s[0]) + System.Text.RegularExpressions.Regex.Replace(s[1..], @"\s+", " ");
        return (Party(m.Groups["g"].Value), Party(m.Groups["b"].Value));
    }

    private static void Indemnification(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "indemnis", "indemnify", "tenir indemne", "dégagera de toute responsabilité", "hold harmless")))
        {
            var uncapped = Has(c, "sans limite", "toute nature", "without limit", "of any kind", "sans plafond");
            var explanation = uncapped
                ? l.T("The indemnity covers claims of any kind, with no limit of amount or duration.",
                      "La garantie couvre toutes réclamations de toute nature, sans limite de montant ni de durée.")
                : l.T("An indemnification clause was detected.", "Une clause d'indemnisation a été détectée.");
            if (IndemnityParties(c.Heading + " " + c.Text) is var (giver, beneficiary))
                explanation += " " + l.T($"Here {giver} indemnifies {beneficiary}: the risk lies with {giver}.",
                                         $"Ici, {giver} garantit {beneficiary} : le risque pèse sur {giver}.");
            findings.Add(Finding(c, "indemnification",
                l.T(uncapped ? "Uncapped indemnity" : "Indemnity", uncapped ? "Garantie sans limite" : "Indemnisation"),
                uncapped ? High : Medium,
                explanation,
                l.T("Limit the indemnity to claims caused by the party's own breach, with a cap and a time limit.",
                    "Limiter la garantie aux réclamations causées par un manquement de la partie, avec un plafond et une durée.")));
        }
    }

    private static readonly Regex PercentPerDay = new(
        @"(\d+(?:[.,]\d+)?)\s*(?:%|pour\s*cent|percent)[^.]{0,60}?par\s+jour|(\d+(?:[.,]\d+)?)\s*%[^.]{0,40}?per\s+day",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void Penalty(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "pénalité", "clause pénale", "dommages-intérêts forfaitaires", "penalty", "liquidated damages")))
        {
            var uncapped = Has(c, "sans plafond", "sans limite", "uncapped", "without cap", "without limit");
            var capped = !uncapped && Has(c, "plafonn", "dans la limite de", "maximum", "capped", "cap of");
            var perDay = PercentPerDay.Match(c.Text);
            var rate = perDay.Success ? (perDay.Groups[1].Success ? perDay.Groups[1].Value : perDay.Groups[2].Value) : null;

            var severity = uncapped ? High : capped ? Low : Medium;
            var detail = uncapped
                ? l.T($"The penalty has no cap{(rate is null ? "" : $": {rate}% of the price per day")} — after {(rate is null ? "enough days" : DaysToFull(rate) + " days")} it exceeds the whole contract value.",
                      $"La pénalité n'a pas de plafond{(rate is null ? "" : $" : {rate} % du prix par jour")}, elle dépasse la valeur totale du contrat au bout de {(rate is null ? "quelques semaines" : DaysToFull(rate) + " jours")}.")
                : capped
                    ? l.T("A capped penalty clause was detected.", "Une clause pénale plafonnée a été détectée.")
                    : l.T("A penalty clause was detected; check that it is capped.", "Une clause pénale a été détectée ; vérifiez qu'elle est plafonnée.");

            findings.Add(Finding(c, "penalty", l.T("Penalty clause", "Clause pénale"), severity, detail,
                capped ? null : l.T("Cap the total penalty (commonly 5 to 10% of the price).", "Plafonner le total des pénalités (couramment 5 à 10 % du prix).")));
        }
    }

    private static string DaysToFull(string rate) =>
        double.TryParse(rate.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && r > 0
            ? Math.Ceiling(100 / r).ToString(CultureInfo.InvariantCulture)
            : "?";

    private static readonly string[] TerminationWords =
        ["terminat", "résiliation", "résilier", "résilié", "resiliation", "rupture du contrat", "fin du contrat"];

    private static void Termination(string text, List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        if (!TerminationWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new RiskFinding(l.T("Termination rights", "Droit de résiliation"), High,
                l.T("The contract has no termination clause.", "Le contrat ne prévoit aucune clause de résiliation."),
                RecommendedAction: l.T("Add termination for breach, with notice, for both parties.",
                                       "Prévoir une résiliation pour manquement, avec préavis, ouverte aux deux parties."),
                RuleKey: "termination"));
            return;
        }

        foreach (var c in clauses.Where(c => Has(c, "aucun droit de résiliation", "ne peut pas résilier", "ne pourra résilier",
                     "no right to terminate", "may not terminate", "shall not be entitled to terminate")))
        {
            findings.Add(Finding(c, "termination", l.T("One-sided termination", "Résiliation unilatérale"), High,
                l.T("Only one party may terminate; the other is bound for as long as the contract renews.",
                    "Une seule partie peut résilier ; l'autre reste engagée aussi longtemps que le contrat se renouvelle."),
                l.T("Give both parties a termination right for breach and on notice at renewal.",
                    "Ouvrir aux deux parties la résiliation pour manquement et à l'échéance, avec préavis.")));
        }
    }

    private static void Renewal(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "tacite reconduction", "reconduit tacitement", "renouvellement automatique", "renouvelé automatiquement",
                     "renouvellera automatiquement", "renouvellement tacite", "se renouvelle", "automatic renewal", "automatically renew", "tacit renewal")))
        {
            var notice = Has(c, "préavis", "notice", "dénonc");
            findings.Add(Finding(c, "auto_renewal", l.T("Automatic renewal", "Reconduction tacite"), Medium,
                notice
                    ? l.T("The contract renews automatically unless notice is given.", "Le contrat se renouvelle automatiquement sauf dénonciation.")
                    : l.T("The contract renews automatically and no notice period to stop it is stated.",
                          "Le contrat se renouvelle automatiquement et aucun délai de préavis pour l'arrêter n'est prévu."),
                notice ? null : l.T("State a notice period (e.g. 3 months before the renewal date).", "Prévoir un délai de préavis (par exemple 3 mois avant l'échéance).")));
        }
    }

    private static void Exclusivity(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "exclusivit", "exclusive", "non-concurrence", "non concurrence", "non-compete")))
        {
            var broad = Has(c, "marché tunisien", "tout le marché", "sur ce marché", "tous ses autres clients", "entire market", "all its other customers", "territoire");
            findings.Add(Finding(c, "exclusivity",
                l.T(broad ? "Market-wide exclusivity" : "Exclusivity", broad ? "Exclusivité sur tout un marché" : "Exclusivité"),
                broad ? High : Medium,
                broad
                    ? l.T("The exclusivity / non-compete covers a whole market: it may be an anti-competitive agreement, void by law.",
                          "L'exclusivité ou la non-concurrence couvre tout un marché : elle peut constituer une entente anticoncurrentielle, nulle de plein droit.")
                    : l.T("An exclusivity or non-compete restriction was detected.", "Une restriction d'exclusivité ou de non-concurrence a été détectée."),
                l.T("Limit exclusivity in scope, territory and duration.", "Limiter l'exclusivité en périmètre, territoire et durée.")));
        }
    }

    private static void UnilateralChange(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "seule discrétion", "sole discretion", "discrétion exclusive")
                                             && Has(c, "modifi", "change", "amend")))
        {
            findings.Add(Finding(c, "unilateral_change", l.T("Unilateral change", "Modification unilatérale"), Medium,
                l.T("One party can change the scope alone" + (Has(c, "sans préavis", "without notice") ? ", without notice." : "."),
                    "Une partie peut modifier seule le périmètre" + (Has(c, "sans préavis", "without notice") ? ", sans préavis." : ".")),
                l.T("Require a written amendment agreed by both parties, with price adjustment.",
                    "Exiger un avenant écrit accepté par les deux parties, avec ajustement du prix.")));
        }
    }

    private static readonly string[] ForeignParty =
        ["constituée à l'étranger", "constituée à l’étranger", "société étrangère", "de droit étranger", "établie à l'étranger",
         "foreign company", "incorporated outside", "foreign entity"];

    private static void PersonalData(string text, List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        var c = clauses.FirstOrDefault(c => Has(c, "données personnelles", "données à caractère personnel", "donnees personnelles", "personal data"));
        if (c is null) return;

        var foreign = ForeignParty.Any(f => text.Contains(f, StringComparison.OrdinalIgnoreCase));
        var safeguards = Has(c, "consentement", "sécurité", "confidentialité", "consent", "security", "confidential");

        var missing = new List<string>();
        if (!Has(c, "consentement", "consent")) missing.Add(l.T("consent", "consentement"));
        if (!Has(c, "sécurité", "security", "mesures techniques")) missing.Add(l.T("security", "sécurité"));
        if (!Has(c, "sous-trait", "subcontract", "processor")) missing.Add(l.T("subcontracting", "sous-traitance"));

        var detail = l.T("The contract involves personal data", "Le contrat prévoit un traitement de données personnelles")
                     + (missing.Count > 0 ? l.T($" but says nothing about {string.Join(", ", missing)}", $" sans rien prévoir sur : {string.Join(", ", missing)}") : "")
                     + (foreign ? l.T("; a party is established abroad, so a transfer abroad needs prior authorisation.",
                                      " ; une partie est établie à l'étranger, tout transfert vers l'étranger exige une autorisation préalable.")
                                : ".");

        findings.Add(Finding(c, "personal_data", l.T("Personal data", "Données personnelles"),
            foreign || !safeguards ? High : Medium, detail,
            l.T("Add a data-processing clause: purpose, consent, security measures, subcontracting, and transfer authorisation.",
                "Ajouter une clause de traitement des données : finalité, consentement, mesures de sécurité, sous-traitance et autorisation de transfert.")));
    }

    private static void ForceMajeure(string text, List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        var c = clauses.FirstOrDefault(c => Has(c, "force majeure", "cas fortuit", "act of god"));
        if (c is null)
        {
            findings.Add(new RiskFinding(l.T("No force majeure clause", "Pas de clause de force majeure"), Low,
                l.T("The contract does not address force majeure: the statutory regime applies by default.",
                    "Le contrat ne traite pas de la force majeure : le régime légal s'applique par défaut."),
                RecommendedAction: l.T("Add a force majeure clause listing the events, the notice to give and the right to terminate if it lasts.",
                                       "Ajouter une clause de force majeure : événements visés, notification et droit de résilier si elle se prolonge."),
                RuleKey: "force_majeure"));
            return;
        }
        if (Has(c, "épidémie", "pandémie", "grève", "cyber", "panne", "epidemic", "strike"))
            findings.Add(Finding(c, "force_majeure", l.T("Extended force majeure", "Force majeure élargie"), Low,
                l.T("The clause adds events to force majeure; check they meet the legal test (unforeseeable, unavoidable, making performance impossible).",
                    "La clause ajoute des événements à la force majeure ; vérifiez qu'ils répondent à la définition légale (imprévisible, irrésistible, rendant l'exécution impossible)."),
                null));
    }

    private static void ElectronicSignature(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "signature électronique", "signé électroniquement", "électroniquement signé", "electronic signature", "signed electronically")).Take(1))
        {
            var certified = Has(c, "certifi", "tuntrust", "agence nationale de certification");
            findings.Add(Finding(c, "electronic_signature", l.T("Electronic signature", "Signature électronique"), certified ? Low : Medium,
                certified
                    ? l.T("The contract is signed electronically with a certified signature.", "Le contrat est signé électroniquement avec une signature certifiée.")
                    : l.T("The contract relies on an electronic signature without saying it is certified or how the signed document is kept.",
                          "Le contrat prévoit une signature électronique sans préciser qu'elle est certifiée ni comment le document signé est conservé."),
                certified ? null : l.T("Use a certified electronic signature and state how the signed version is archived.",
                                       "Utiliser une signature électronique certifiée et préciser l'archivage de la version signée.")));
        }
    }

    private static void CommercialAgency(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "agent commercial", "agence commerciale", "commercial agent", "commercial agency")).Take(1))
        {
            var notice = Has(c, "préavis", "notice");
            findings.Add(Finding(c, "commercial_agency", l.T("Commercial agency", "Agence commerciale"), notice ? Low : Medium,
                notice
                    ? l.T("A commercial agency relationship with a notice period.", "Une relation d'agence commerciale avec un préavis.")
                    : l.T("A commercial agency relationship with no notice period: an open-ended agency cannot be ended without notice.",
                          "Une relation d'agence commerciale sans préavis : une agence à durée indéterminée ne peut être rompue sans préavis."),
                notice ? null : l.T("State the notice period for termination.", "Prévoir le préavis de résiliation.")));
        }
    }

    private static void ConsumerOnlineSale(string text, List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        var online = new[] { "en ligne", "site internet", "site web", "commande électronique", "voie électronique", "online", "website" }
            .Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
        var consumer = new[] { "consommateur", "consumer" }.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
        if (!online || !consumer) return;

        var withdrawal = new[] { "rétractation", "retractation", "withdrawal", "cooling-off" }.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
        findings.Add(new RiskFinding(l.T("Online sale to consumers", "Vente en ligne au consommateur"), withdrawal ? Low : High,
            withdrawal
                ? l.T("Online sale to consumers: check the pre-contract information and the withdrawal terms.",
                      "Vente en ligne au consommateur : vérifiez l'information précontractuelle et les modalités de rétractation.")
                : l.T("Online sale to consumers with no withdrawal right stated: the consumer has a statutory right to withdraw.",
                      "Vente en ligne au consommateur sans droit de rétractation : le consommateur dispose d'un droit légal de rétractation."),
            RecommendedAction: l.T("State the pre-contract information and the 10-working-day withdrawal right.",
                                   "Prévoir l'information précontractuelle et le droit de rétractation de 10 jours ouvrables."),
            RuleKey: "consumer_online"));
    }

    private static void LateInterestWaived(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "aucun intérêt", "pas d'intérêt", "sans intérêt", "no interest")
                                             && Has(c, "retard", "late")))
        {
            findings.Add(Finding(c, "payment", l.T("Late-payment interest excluded", "Intérêts de retard exclus"), Medium,
                l.T("The clause excludes any interest or cost on late payment: the creditor gives up the statutory interest.",
                    "La clause exclut tout intérêt ou frais en cas de retard de paiement : le créancier renonce aux intérêts légaux."),
                l.T("Provide late-payment interest and recovery costs.", "Prévoir des intérêts de retard et des frais de recouvrement.")));
        }
    }

    private static void Deposit(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "arrhes", "acompte", "deposit", "down payment")).Take(1))
        {
            var forfeited = Has(c, "non restituable", "non remboursable", "définitivement acquis", "reste acquis", "non-refundable");
            findings.Add(Finding(c, "deposit", l.T("Deposit", "Arrhes / acompte"), forfeited ? Medium : Low,
                forfeited
                    ? l.T("The deposit is kept in every case, whoever is at fault.", "Les arrhes restent acquises dans tous les cas, quelle que soit la partie en faute.")
                    : l.T("The contract provides a deposit: check how it is credited or returned.", "Le contrat prévoit des arrhes ou un acompte : vérifiez leur imputation et leur restitution."),
                forfeited ? l.T("Keep the deposit only if the paying party is at fault.", "Ne conserver les arrhes qu'en cas de faute de la partie qui les a versées.") : null));
        }
    }

    private static void BreachWithoutNotice(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "résili", "résolu", "terminat")
                                             && Has(c, "manquement", "inexécution", "faute", "breach", "default")
                                             && !Has(c, "mise en demeure", "mis en demeure", "préavis", "notice", "notification")))
        {
            findings.Add(Finding(c, "breach_notice", l.T("Termination for breach without formal notice", "Résiliation pour manquement sans mise en demeure"), Medium,
                l.T("The contract can be ended for breach without first giving the other party formal notice to perform.",
                    "Le contrat peut être résilié pour manquement sans mise en demeure préalable de l'autre partie."),
                l.T("Require formal notice with a period to cure before termination.", "Exiger une mise en demeure avec un délai pour remédier avant la résiliation.")));
        }
    }

    private static void Suspension(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "suspendre", "suspension de", "suspend")).Take(1))
        {
            findings.Add(Finding(c, "suspension", l.T("Suspension of performance", "Suspension de l'exécution"), Low,
                l.T("One party may suspend performance: check it is tied to the other party's own failure to perform.",
                    "Une partie peut suspendre l'exécution : vérifiez que c'est lié à une inexécution de l'autre partie."),
                null));
        }
    }

    private static void WorksGuarantee(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        var c = clauses.FirstOrDefault(c => Has(c, "travaux", "chantier", "construction", "ouvrage", "works", "building"));
        if (c is null) return;
        var excluded = clauses.Any(x => Has(x, "aucune garantie", "sans garantie", "exclut toute garantie", "no warranty", "without warranty"));
        findings.Add(Finding(c, "works_guarantee", l.T("Works guarantee", "Garantie de l'ouvrage"), excluded ? High : Low,
            excluded
                ? l.T("A works contract that excludes any guarantee: the contractor's statutory guarantees cannot simply be removed.",
                      "Un contrat d'ouvrage qui exclut toute garantie : les garanties légales de l'entrepreneur ne peuvent pas simplement être écartées.")
                : l.T("A works contract: the contractor's statutory guarantees apply.", "Un contrat d'ouvrage : les garanties légales de l'entrepreneur s'appliquent."),
            excluded ? l.T("Remove the exclusion and state the guarantee periods.", "Supprimer l'exclusion et préciser les durées de garantie.") : null));
    }

    private static void Transport(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        var c = clauses.FirstOrDefault(c => Has(c, "transporteur", "contrat de transport", "transport de marchandises", "transport des marchandises", "carrier", "carriage of goods"));
        if (c is null) return;
        var exonerated = clauses.Any(x => Has(x, "exonér", "aucune responsabilité", "décline toute responsabilité", "no liability", "not be liable"));
        findings.Add(Finding(c, "transport", l.T("Transport", "Transport"), exonerated ? High : Low,
            exonerated
                ? l.T("A transport contract with a clause excluding the carrier's liability.", "Un contrat de transport avec une clause écartant la responsabilité du transporteur.")
                : l.T("A transport contract: the carrier is liable from the moment it receives the goods.", "Un contrat de transport : le transporteur est responsable dès la remise de la marchandise."),
            exonerated ? l.T("Check the exclusion against the carrier's statutory liability.", "Confronter l'exclusion à la responsabilité légale du transporteur.") : null));
    }

    private static void ElectronicPayment(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "paiement électronique", "monnaie électronique", "transfert électronique", "carte bancaire", "electronic payment", "e-money")).Take(1))
        {
            findings.Add(Finding(c, "electronic_payment", l.T("Electronic payment", "Paiement électronique"), Low,
                l.T("Payment by electronic transfer: the issuer has information, security and record-keeping duties.",
                    "Paiement par transfert électronique : l'émetteur a des obligations d'information, de sécurité et de conservation."),
                null));
        }
    }

    private static readonly Regex BankParty = new(
        @"\b(?:la|une|cette|de\s+la)\s+banque\b|établissement\s+financier|\bthe\s+bank\b|financial\s+institution",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsBankContract(string text) => BankParty.IsMatch(text);

    private static bool TextHas(string text, params string[] terms) =>
        terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    /// <summary>A bank outsourcing activities to a provider: the contract must keep the central bank able to supervise.</summary>
    private static void BankOutsourcing(string text, List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        if (!IsBankContract(text)) return;
        var c = clauses.FirstOrDefault(c => Has(c, "externalis", "sous-trait", "hébergement", "infogérance", "prestation de services",
            "services informatiques", "outsourc", "hosting", "managed services"));
        if (c is null) return;

        var supervision = TextHas(text, "banque centrale", "BCT", "central bank");
        var audit = TextHas(text, "audit", "contrôle sur place", "droit d'accès", "inspection");
        if (supervision && audit) return;

        var missing = new List<string>();
        if (!supervision) missing.Add(l.T("access for the central bank's supervision", "l'accès de la Banque centrale pour sa supervision"));
        if (!audit) missing.Add(l.T("the bank's audit rights", "le droit d'audit de la banque"));

        findings.Add(Finding(c, "bank_outsourcing", l.T("Bank outsourcing", "Externalisation bancaire"), High,
            l.T($"The bank outsources part of its activity, but the contract does not provide {string.Join(" or ", missing)}. The central bank must also be informed before signing.",
                $"La banque externalise une partie de son activité, mais le contrat ne prévoit pas {string.Join(" ni ", missing)}. La Banque centrale doit aussi être informée avant la signature."),
            l.T("Add a clause guaranteeing the bank's audit rights and the central bank's supervision of the outsourced operations, and notify the central bank before signing.",
                "Ajouter une clause garantissant le droit d'audit de la banque et la supervision par la Banque centrale des opérations externalisées, et informer la Banque centrale avant la signature.")));
    }

    /// <summary>A provider handling a bank's client information must be bound to the bank's professional secrecy.</summary>
    private static void BankSecrecy(string text, List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        if (!IsBankContract(text)) return;
        var c = clauses.FirstOrDefault(c => Has(c, "données", "informations", "fichiers", "data", "information"));
        if (c is null || TextHas(text, "secret professionnel", "secret bancaire", "banking secrecy", "professional secrecy")) return;

        findings.Add(Finding(c, "bank_secrecy", l.T("Banking secrecy", "Secret bancaire"), High,
            l.T("The provider will handle the bank's information, but the contract does not bind it to professional secrecy.",
                "Le prestataire aura accès aux informations de la banque, mais le contrat ne le soumet pas au secret professionnel."),
            l.T("Bind the provider and its staff to professional secrecy, including after the contract ends.",
                "Soumettre le prestataire et son personnel au secret professionnel, y compris après la fin du contrat.")));
    }

    /// <summary>An account agreement must list the commissions and tell the client about the banking mediator.</summary>
    private static void BankAccountAgreement(string text, List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        if (!TextHas(text, "convention de compte", "ouverture de compte", "compte de dépôt", "account agreement", "deposit account")) return;
        var c = clauses.FirstOrDefault(c => Has(c, "compte", "account")) ?? clauses.FirstOrDefault();
        if (c is null) return;

        var missing = new List<string>();
        if (!TextHas(text, "commission", "tarif", "frais", "fees")) missing.Add(l.T("the list and amount of commissions", "la liste et le montant des commissions"));
        if (!TextHas(text, "médiat", "mediat")) missing.Add(l.T("the banking mediator and how to refer to it", "le médiateur bancaire et ses modalités de saisine"));
        if (missing.Count == 0) return;

        findings.Add(Finding(c, "bank_account", l.T("Account agreement", "Convention de compte"), High,
            l.T($"The account agreement does not state {string.Join(" or ", missing)}.", $"La convention de compte ne mentionne pas {string.Join(" ni ", missing)}."),
            l.T("Add the commission schedule and a mediation clause.", "Ajouter le barème des commissions et une clause de médiation bancaire.")));
    }

    // ------------------------------------------------------------------ structural checks

    private static readonly Regex DaysFigure = new(@"(\d{1,3})\s*(?:jours|days)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void ContradictoryPaymentTerms(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        string[] payWords = ["payable", "paiement", "payment", "paiera", "paid", "réglé", "règlement"];
        foreach (var c in clauses)
        {
            // Compare only figures in sentences about payment: a notice period of 15 days is not a payment term.
            var days = Regex.Split(c.Text, @"(?<=[.;])\s+")
                .Where(s => payWords.Any(w => s.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .SelectMany(s => DaysFigure.Matches(s).Select(m => int.Parse(m.Groups[1].Value)))
                .Distinct().ToList();
            if (days.Count < 2) continue;
            var list = string.Join(" / ", days.Select(d => l.T($"{d} days", $"{d} jours")));
            findings.Add(Finding(c, "contradiction", l.T("Contradictory payment terms", "Délais de paiement contradictoires"), High,
                l.T($"The clause sets several payment periods ({list}); it is unclear which one applies.",
                    $"La clause fixe plusieurs délais de paiement ({list}) ; on ne sait pas lequel s'applique."),
                l.T("Keep a single payment period and state late-payment interest.",
                    "Retenir un seul délai de paiement et prévoir des intérêts de retard.")));
        }
    }

    // Only references to this contract's own articles: "l'article 14 de la loi" points elsewhere and is left alone.
    private static readonly Regex ArticleReference = new(
        @"article\s+(\d+)\s+(?:du\s+présent|du\s+contrat|de\s+ce\s+contrat|ci-dessus|ci-dessous|of\s+this|hereof|above|below)|clause\s+(\d+)\s+(?:du\s+présent|of\s+this|hereof)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void MissingReferences(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        var numbers = clauses.Where(c => c.Number.HasValue).Select(c => c.Number!.Value).ToHashSet();
        if (numbers.Count < 3) return;

        foreach (var c in clauses)
        {
            foreach (Match m in ArticleReference.Matches(c.Text))
            {
                var n = int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (numbers.Contains(n) || n == c.Number) continue;
                findings.Add(Finding(c, "contradiction", l.T("Reference to a missing article", "Renvoi à un article inexistant"), High,
                    l.T($"The clause refers to Article {n}, which the contract does not contain (it stops at Article {numbers.Max()}).",
                        $"La clause renvoie à l'article {n}, qui n'existe pas dans le contrat (il s'arrête à l'article {numbers.Max()})."),
                    l.T("Fix the reference or add the missing article.", "Corriger le renvoi ou ajouter l'article manquant.")));
            }
        }
    }

    private static readonly Regex CompanyName = new(
        @"\b((?:\p{Lu}[\p{L}&'-]*\s+){1,3})(SARL|SUARL|SAS|SA|Ltd|LLC|Inc|GmbH|Group|Groupe)\b", RegexOptions.Compiled);

    private static void InconsistentParties(string text, Lang l, List<RiskFinding> findings)
    {
        var names = CompanyName.Matches(text)
            .Select(m => $"{m.Groups[1].Value.Trim()} {m.Groups[2].Value}")
            .Distinct()
            .ToList();

        foreach (var group in names.GroupBy(n => n.Split(' ')[0]).Where(g => g.Count() > 1))
        {
            findings.Add(new RiskFinding(l.T("Inconsistent party names", "Désignation des parties incohérente"), Medium,
                l.T($"The same party is named in different ways: {string.Join(", ", group)}. The contract may not bind the right legal entity.",
                    $"Une même partie est désignée de plusieurs façons : {string.Join(", ", group)}. Le contrat risque de ne pas engager la bonne personne morale."),
                RecommendedAction: l.T("Use one exact legal name per party, with its registration number.",
                                       "Utiliser une seule dénomination exacte par partie, avec son numéro d'immatriculation."),
                ContractReference: l.T("Parties", "Parties"),
                RuleKey: "contradiction"));
        }
    }

    // ------------------------------------------------------------------ helpers

    // A heading is "ARTICLE 6 - …" / "Article 6 : …": the number is followed by a separator. A reference such as
    // "l'Article 14 du présent Contrat" has none, so it never splits a clause.
    private static readonly Regex InlineHeading = new(
        @"(?<=\S)\s+(?=(?i:article|clause)\s+\d+\s*[-–—:]\s)", RegexOptions.Compiled);

    /// <summary>
    /// Clauses of a contract whatever the text extraction did with line breaks: PDF text often arrives as one line per
    /// page, so headings are also found mid-line.
    /// </summary>
    public static List<string> SplitClauses(string? text) =>
        ContractDiffer.SplitClauses(text is null ? null : InlineHeading.Replace(text, "\n\n"));

    // Case-sensitive on purpose: the title is the run of capitals ("PÉNALITÉS") before the first sentence ("Pour …").
    private static readonly Regex Heading = new(
        @"^\s*(?i:article|art\.|clause|section)\s*(\d+)\s*[-–—:.)]?\s*([\p{Lu}\p{N}\s'’&/,-]{2,60}?)(?=\s+\p{Lu}\p{Ll}|\s*$)",
        RegexOptions.Compiled);

    private static Clause ToClause(string text)
    {
        var m = Heading.Match(text);
        if (!m.Success) return new Clause(text.Length > 50 ? text[..50] + "…" : text, text, null);
        var title = m.Groups[2].Value.Trim();
        var label = $"Article {m.Groups[1].Value}" + (title.Length > 0 ? $" — {Capitalize(title)}" : "");
        return new Clause(label, text, int.Parse(m.Groups[1].Value));
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..].ToLower(CultureInfo.InvariantCulture);

    private static bool Has(Clause c, params string[] terms) =>
        terms.Any(t => c.Text.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static RiskFinding Finding(Clause c, string? ruleKey, string title, string severity, string explanation, string? action) =>
        new(title, severity, explanation, RecommendedAction: action, ContractReference: c.Heading, RuleKey: ruleKey);
}
