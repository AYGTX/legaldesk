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
        BankRelatedParty(text, clauses, l, findings);
        BankSupervisorAccess(text, clauses, l, findings);
        BankCustomerComplaints(text, clauses, l, findings);
        BankAntiMoneyLaundering(text, clauses, l, findings);
        ImposedResalePrice(clauses, l, findings);
        MarketSharing(clauses, l, findings);

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
                l.T($"The risk grid flags the wording “{phrase.Trim()}” for this risk.",
                    $"La grille des risques signale la formulation « {phrase.Trim()} » pour ce risque."),
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

    /// <summary>Finding type of a meaning-based lead: shown apart from certain findings, and not counted in the score.</summary>
    public const string Possible = "Possible";

    /// <summary>The clauses as the review names them ("Article 5 — Responsabilité"), for services that work clause by clause.</summary>
    public static IReadOnlyList<ClauseText> ClauseTexts(string? text) =>
        SplitClauses(text).Select(ToClause).Select(c => new ClauseText(c.Heading, c.Text)).ToList();

    /// <summary>
    /// Adds a "possible risk" finding for each meaning-based match on a clause where no finding already raised that
    /// rule. Possible findings are leads for a reviewer: grouped apart in the UI and left out of the risk score.
    /// </summary>
    public static ContractRiskAnalysis ApplyPossibleMatches(ContractRiskAnalysis risk, string? text, IReadOnlyList<ClauseMatch> matches)
    {
        if (matches.Count == 0) return risk;
        var fr = ContractLanguage.Detect(text) == "fr";
        var l = new Lang(fr);
        var findings = risk.Findings.ToList();
        var added = 0;
        foreach (var m in matches)
        {
            if (findings.Any(f => f.RuleKey == m.RuleKey && f.ContractReference == m.Reference)) continue;
            var score = m.Score.ToString("0.00", fr ? CultureInfo.GetCultureInfo("fr-FR") : CultureInfo.InvariantCulture);
            findings.Add(new RiskFinding(
                l.T($"Possible risk: {m.Label}", $"Risque possible : {m.Label}"), Low,
                l.T($"This sentence reads like an approved example of this risk in the risk grid (similarity {score}): “{m.Sentence}”",
                    $"Cette phrase ressemble à un exemple validé de ce risque dans la grille des risques (similarité {score}) : « {m.Sentence} »"),
                FindingType: Possible,
                RecommendedAction: l.T($"Check whether the clause carries the “{m.Label}” risk. Closest example: “{m.Example}”",
                                       $"Vérifier si la clause porte le risque « {m.Label} ». Exemple le plus proche : « {m.Example} »"),
                ContractReference: m.Reference,
                RuleKey: m.RuleKey));
            added++;
        }
        if (added == 0) return risk;
        findings.RemoveAll(f => f.RuleKey is null && f.Severity == Low && f.ContractReference is null
                                && (f.ClauseType == "General" || f.ClauseType == "Général"));
        return risk with { Findings = Order(findings), RiskScore = Score(findings) };
    }

    private static List<RiskFinding> Order(List<RiskFinding> findings) =>
        findings.OrderBy(f => f.Severity switch { High => 0, Medium => 1, _ => 2 }).ToList();

    private static int Score(IEnumerable<RiskFinding> findings) =>
        Math.Min(100, findings.Where(f => f.FindingType != Possible).Sum(f => f.Severity switch { High => 20, Medium => 10, _ => 2 }));

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

        // A cap that leaves whole categories of claims outside it: "The Liability Cap will not apply to indemnification
        // obligations or breach of confidentiality". Exceptions for fraud or gross negligence alone are standard (and
        // required in Tunisian law), so they are not flagged.
        foreach (var c in clauses.Where(c => findings.All(f => f.ContractReference != c.Heading || f.RuleKey != "liability")
                                             && Has(c, "shall not exceed", "will not exceed", "not to exceed", "limited to", "aggregate liability",
                                                 "total liability", "liability cap", "ne saurait excéder", "ne pourra excéder", "ne pourra dépasser",
                                                 "plafonnée", "limitée au montant", "limitée à")
                                             && CapCarveOut.IsMatch(c.Text)
                                             && Has(c, "indemnif", "confidential", "intellectual property", "infringement", "data protection",
                                                 "indemnisation", "garantie", "confidentialité", "propriété intellectuelle", "contrefaçon", "données")))
        {
            findings.Add(Finding(c, "liability", l.T("Liability cap with broad exceptions", "Plafond de responsabilité assorti d'exceptions larges"), Medium,
                l.T("Liability is capped, but whole categories of claims (indemnities, confidentiality, intellectual property or data) are left outside the cap, uncapped.",
                    "La responsabilité est plafonnée, mais des catégories entières de réclamations (garanties, confidentialité, propriété intellectuelle ou données) échappent au plafond et restent illimitées."),
                l.T("Give the excluded claims their own cap (e.g. a multiple of the contract price), keeping only fraud and gross negligence uncapped.",
                    "Prévoir un plafond propre pour les réclamations exclues (par exemple un multiple du prix du contrat), en ne laissant sans plafond que le dol et la faute lourde.")));
        }
    }

    private static readonly Regex CapCarveOut = new(
        @"(?:shall|will|does)\s+not\s+apply\s+to|except\s+(?:for|with\s+regard\s+to|in\s+connection\s+with|in\s+the\s+event\s+of|as\s+to)|excluding|other\s+than"
        + @"|ne\s+s['’]applique\s+pas|sauf\s+(?:en\s+cas\s+d['’e]|pour|s['’]agissant)|à\s+l['’]exception\s+d",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    // "shall renew automatically", "shall be automatically renewed/extended", "will automatically extend", "for successive
    // renewal terms", "unless either party gives notice of its intention not to renew".
    private static readonly Regex EnglishRenewal = new(
        @"\b(?:renew|extend)\w*\s+automatically\b|\bautomatically\s+(?:be\s+)?(?:renew|extend)\w*|\b(?:successive|additional)\s+(?:renewal\s+)?(?:terms?|periods?)\s+of\b"
        + @"|\bunless\s+(?:either|one|a)\s+party\b[^.]{0,160}\b(?:not\s+to\s+renew|non-?renewal|terminat\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void Renewal(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(c => Has(c, "tacite reconduction", "reconduit tacitement", "renouvellement automatique", "renouvelé automatiquement",
                     "renouvellera automatiquement", "renouvellement tacite", "se renouvelle", "automatic renewal", "automatically renew", "tacit renewal")
                     || EnglishRenewal.IsMatch(c.Text)))
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

    // Uses of "exclusive" that are not an exclusivity commitment: ownership, remedies, courts, the whole agreement,
    // "non-exclusive" grants, "exclusive of" (meaning "excluding"), "used exclusively for".
    private static readonly Regex BenignExclusive = new(
        @"non[\s-]*exclusi\w*|(?:sole\s+and\s+)?exclusive\s+(?:property|owner\w*|remed\w*|jurisdiction|venue|forum|courts?|benefit|agreement|statement|use\s+of\s+the\s+courts"
        + @"|liability|obligation|warrant\w*|personal\s+jurisdiction|purpose|legal\s+and\s+beneficial|economic\s+zone)"
        + @"|(?:is|be|are)\s+not\s+exclusive|exclusively\s+relat\w*|exclusive\s+and\s+convenient\s+forum"
        + @"|exclusive\s+of\b|exclusively\s+(?:for|in|to|by|on)\b|(?:complete|entire)\s+and\s+exclusive"
        + @"|compétence\s+exclusive|juridiction\s+exclusive|propriété\s+exclusive|seul\s+et\s+exclusif\s+propriétaire|à\s+titre\s+non\s+exclusif|non\s+exclusi\w*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExclusivityWords = new(
        @"exclusivit|exclusi(?:f|ve|vely)\b|non[\s-]*concurrence|non[\s-]*compet|not\s+(?:to\s+)?compete|ne\s+pas\s+concurrencer",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsExclusivityCommitment(Clause c) => ExclusivityWords.IsMatch(BenignExclusive.Replace(c.Text, " "));

    private static void Exclusivity(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        foreach (var c in clauses.Where(IsExclusivityCommitment))
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
    /// <summary>A bank contracting with a shareholder, director or group company: the board must authorise it beforehand,
    /// the shareholders approve it and the central bank is told.</summary>
    private static void BankRelatedParty(string text, List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        if (!IsBankContract(text)) return;
        var c = clauses.FirstOrDefault(c => Has(c, "actionnaire de la banque", "administrateur de la banque", "dirigeant de la banque",
            "filiale de la banque", "société du groupe", "société mère", "société apparentée", "partie liée", "parties liées",
            "shareholder of the bank", "director of the bank", "group company", "related party"));
        if (c is null || TextHas(text, "conseil d'administration", "conseil de surveillance", "assemblée générale",
                "board of directors", "general meeting")) return;

        findings.Add(Finding(c, "bank_related_party", l.T("Related-party agreement", "Convention réglementée"), High,
            l.T("The bank contracts with a person linked to it (shareholder, director or group company), but the contract does not mention the board's prior authorisation or the shareholders' approval.",
                "La banque contracte avec une personne qui lui est liée (actionnaire, dirigeant ou société du groupe), mais le contrat ne mentionne ni l'autorisation préalable du conseil ni l'approbation de l'assemblée générale."),
            l.T("Have the agreement authorised by the board before signing (the interested person not voting), approved by the general meeting, and notified to the central bank.",
                "Faire autoriser la convention par le conseil avant la signature (sans vote de la personne intéressée), la soumettre à l'approbation de l'assemblée générale et en informer la Banque centrale.")));
    }

    /// <summary>Professional secrecy cannot be used against the central bank's supervisors: a bank's confidentiality clause
    /// must leave them out of "any third party".</summary>
    private static void BankSupervisorAccess(string text, List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        if (!IsBankContract(text)) return;
        var c = clauses.FirstOrDefault(c => Has(c, "confidentiel", "ne pas divulguer", "ne divulguer", "à aucun tiers", "aucun tiers",
            "confidential", "not disclose", "no third party"));
        if (c is null || Has(c, "banque centrale", "BCT", "autorités de contrôle", "autorité de contrôle", "autorités de supervision",
                "autorités compétentes", "autorité compétente", "central bank", "supervisory authorit", "competent authorit")) return;

        findings.Add(Finding(c, "bank_supervisor_access", l.T("Confidentiality without a supervisor exception", "Confidentialité sans exception pour la BCT"), Medium,
            l.T("The confidentiality clause forbids disclosure to third parties without excepting the central bank and the supervisory authorities, to whom professional secrecy cannot be opposed.",
                "La clause de confidentialité interdit toute communication aux tiers sans excepter la Banque centrale et les autorités de contrôle, auxquelles le secret professionnel n'est pas opposable."),
            l.T("Add an exception for disclosures to the central bank, the supervisory authorities and any authority entitled by law.",
                "Ajouter une exception pour les communications à la Banque centrale, aux autorités de contrôle et à toute autorité habilitée par la loi.")));
    }

    /// <summary>A customer agreement must tell the client how to complain, as part of the bank's transparency duties.</summary>
    private static void BankCustomerComplaints(string text, List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        if (!TextHas(text, "convention de compte", "ouverture de compte", "compte de dépôt", "conditions générales de banque",
                "account agreement", "deposit account")) return;
        if (TextHas(text, "réclamation", "complaint")) return;
        var c = clauses.FirstOrDefault(c => Has(c, "client", "customer")) ?? clauses.FirstOrDefault();
        if (c is null) return;

        findings.Add(Finding(c, "bank_customer_complaints", l.T("No complaint procedure", "Pas de procédure de réclamation"), Medium,
            l.T("The customer agreement does not say how the client can complain or within what time the bank answers.",
                "La convention ne précise pas comment le client peut adresser une réclamation ni dans quel délai la banque y répond."),
            l.T("Describe the complaint channel, the response time and, after it, the referral to the banking mediator.",
                "Décrire le canal de réclamation, le délai de réponse et, à défaut de solution, la saisine du médiateur bancaire.")));
    }

    // "pendant 5 ans", "pendant cinq (5) ans", "for 5 years": the figure in digits, possibly in brackets after the word.
    private static readonly Regex RetentionYears = new(@"(\d{1,2})\)?\s*(?:ans|années|years)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A provider identifying the bank's customers must itself be bound by anti-money-laundering rules, and customer
    /// records must be kept for at least ten years.</summary>
    private static void BankAntiMoneyLaundering(string text, List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        if (!IsBankContract(text)) return;

        var onboarding = clauses.FirstOrDefault(c => Has(c, "identification des clients", "identifier les clients", "entrée en relation",
            "connaissance du client", "KYC", "pièces d'identité", "apporteur d'affaires", "customer identification", "onboarding", "know your customer"));
        if (onboarding is not null && !TextHas(text, "blanchiment", "financement du terrorisme", "money laundering", "terrorist financing"))
            findings.Add(Finding(onboarding, "bank_aml", l.T("Customer identification by a third party", "Identification des clients confiée à un tiers"), High,
                l.T("A third party identifies the bank's customers, but the contract does not bind it to anti-money-laundering rules or give the bank the identification data.",
                    "Un tiers identifie les clients de la banque, mais le contrat ne le soumet pas aux règles de lutte contre le blanchiment et ne garantit pas à la banque l'accès aux données d'identification."),
                l.T("Require the third party to be subject to anti-money-laundering rules and supervision, to apply the required due diligence, and to hand the bank the identification data without delay.",
                    "Exiger que le tiers soit soumis à une réglementation et à une surveillance en matière de lutte contre le blanchiment, qu'il applique les mesures de diligence requises et qu'il remette sans délai à la banque les données d'identification.")));

        var retention = clauses.FirstOrDefault(c =>
            (Has(c, "conserv", "archiv", "retain", "retention") && RetentionYears.Matches(c.Text).Any(m => int.Parse(m.Groups[1].Value) < 10))
            || (Has(c, "détrui", "destruction", "supprim", "destroy", "delete") && Has(c, "fin du contrat", "expiration", "terme du contrat", "end of the contract", "termination")));
        if (retention is not null && Has(retention, "client", "données", "documents", "dossiers", "records", "data", "customer"))
            findings.Add(Finding(retention, "bank_aml", l.T("Records not kept ten years", "Conservation des documents inférieure à dix ans"), High,
                l.T("The clause has customer records destroyed at the end of the contract or kept for less than ten years, while the bank must keep them for at least ten years.",
                    "La clause prévoit la destruction des documents clients à la fin du contrat ou leur conservation pendant moins de dix ans, alors que la banque doit les conserver au moins dix ans."),
                l.T("Keep (or return to the bank) all records needed to trace operations and identify customers for at least ten years after the operation or the account's closure.",
                    "Conserver (ou restituer à la banque) tous les documents permettant de retracer les opérations et d'identifier les clients pendant au moins dix ans après l'opération ou la clôture du compte.")));
    }

    /// <summary>Fixing a reseller's minimum resale price or minimum margin is prohibited, directly or indirectly.</summary>
    private static void ImposedResalePrice(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        var c = clauses.FirstOrDefault(c =>
            Has(c, "prix de revente", "prix public", "revendre", "marge", "resale price", "resell", "margin")
            && Has(c, "minimum", "minimal", "minimale", "en dessous", "inférieur", "au moins", "ne pourra être", "below", "at least", "no less than"));
        if (c is null) return;

        findings.Add(Finding(c, "resale_price", l.T("Imposed resale price", "Prix de revente imposé"), High,
            l.T("The clause sets a minimum resale price or margin for the reseller, a prohibited practice; the clause is void by law.",
                "La clause fixe au revendeur un prix de revente ou une marge minimum, pratique interdite ; la clause est nulle de plein droit."),
            l.T("Replace the minimum price with a recommended or maximum resale price, leaving the reseller free to set its prices.",
                "Remplacer le prix minimum par un prix de revente conseillé ou maximum, en laissant le revendeur libre de fixer ses prix.")));
    }

    /// <summary>Agreements sharing markets or customers, or setting prices together, are prohibited and void.</summary>
    private static void MarketSharing(List<Clause> clauses, Lang l, List<RiskFinding> findings)
    {
        var c = clauses.FirstOrDefault(c =>
            Has(c, "répartir les marchés", "répartition des marchés", "répartition des clients", "répartir les clients", "se partager",
                "ne pas se concurrencer", "aligner leurs prix", "prix communs", "se concerter sur les prix", "fixer ensemble",
                "share the market", "allocate customers", "not compete with each other", "align their prices")
            || (Has(c, "territoire", "zone", "région", "territory", "region")
                && Has(c, "réservé à", "réservée à", "chacune des parties", "l'autre partie s'interdit", "reserved to", "each party")
                && Has(c, "s'interdit", "s'engage à ne pas", "ne pourra pas", "shall not", "undertakes not to")));
        if (c is null) return;

        findings.Add(Finding(c, "market_sharing", l.T("Market sharing", "Répartition de marché"), High,
            l.T("The clause shares markets, territories or customers between the parties, or has them set prices together: an anticompetitive agreement, void by law.",
                "La clause répartit des marchés, des territoires ou des clients entre les parties, ou leur fait fixer leurs prix ensemble : une entente anticoncurrentielle, nulle de plein droit."),
            l.T("Remove the sharing or price-coordination commitment, or have it checked by competition counsel (exemption, vertical exclusivity justified by the contract).",
                "Supprimer l'engagement de répartition ou de concertation sur les prix, ou le faire valider par un conseil en concurrence (exemption, exclusivité verticale justifiée par le contrat).")));
    }

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
