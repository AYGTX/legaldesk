using System.Text.Json;
using System.Text.RegularExpressions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Serialization;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Common.Services;

/// <summary>A verified article a rule links, as shown beside a finding (R-M8-11).</summary>
/// <param name="Explanation">A lawyer-approved plain-language reading, in the contract's language; shown after the quote,
/// never instead of it.</param>
public record ReviewArticle(string ArticleId, string Citation, string Quote, string? Why, string? Explanation);

/// <summary>One item's worth of findings: every hit of one rule in one clause (R-M8-3).</summary>
public record ContractFinding(
    string RuleKey,
    string? ClauseReference,
    string Severity,
    string Label,
    string Explanation,
    string? RecommendedAction,
    string? ProposedLanguage,
    IReadOnlyList<ReviewArticle> Articles,
    int? RuleVersion,
    string? RuleApprovedBy,
    DateTime? RuleApprovedAt,
    IReadOnlyList<string> Quotes,
    string? DetectionKind,
    double? Confidence)
{
    public ReviewFinding ToDomain() => new(
        RuleKey, ClauseReference, Severity, Label, Explanation, RecommendedAction, ProposedLanguage,
        Articles.Count == 0 ? null : JsonSerializer.Serialize(Articles, ContractJson.Options),
        RuleVersion, RuleApprovedBy, RuleApprovedAt,
        Quotes.Count == 0 ? null : JsonSerializer.Serialize(Quotes, ContractJson.Options),
        DetectionKind, Confidence);
}

/// <param name="GridVersion">Harvey's fingerprint of the rule versions used.</param>
public record ContractReview(string Language, string? GridVersion, IReadOnlyList<ContractFinding> Findings);

public interface IContractReviewService
{
    /// <summary>
    /// Harvey's review of <paramref name="text"/> with the approved rules, the organisation's settings applied. Throws
    /// when Harvey cannot answer: there is no fallback to a model or to keywords (R-M8-9).
    /// </summary>
    Task<ContractReview> ReviewAsync(
        string text,
        IReadOnlyCollection<string> disabledRules,
        IReadOnlyDictionary<string, string> severityOverrides,
        CancellationToken ct = default);
}

/// <summary>
/// The only source of findings (D1, R-M8-1): Harvey reviews the clauses with approved rule versions, and the grid in
/// service supplies each rule's verified articles, approved model clause and approver (R-M8-2). No model is involved,
/// so the same text, grid version and settings always give the same findings (R-M8-10).
/// </summary>
public class ContractReviewService(ILegalCorpusClient harvey) : IContractReviewService
{
    private const int MaxQuoteChars = 700;
    private const int ShortQuoteChars = 320;

    public async Task<ContractReview> ReviewAsync(
        string text,
        IReadOnlyCollection<string> disabledRules,
        IReadOnlyDictionary<string, string> severityOverrides,
        CancellationToken ct = default)
    {
        var detected = ContractLanguage.Detect(text);
        var language = detected is "fr" or "en" ? detected : null;

        var review = await harvey.ReviewAsync(ClauseSplitter.ClauseTexts(text), language, disabledRules, severityOverrides, ct);
        var playbook = await harvey.GetPlaybookAsync(ct);

        return new ContractReview(review.Language, review.GridVersion, Group(review.Findings, playbook, review.Language));
    }

    /// <summary>
    /// One finding per (rule, clause), in a fixed order: most severe first, then in the order the clauses came back.
    /// Two hits of one rule in one clause keep both quotes; the strongest detection and confidence are kept.
    /// </summary>
    public static List<ContractFinding> Group(IReadOnlyList<HarveyFinding> findings, LegalPlaybook playbook, string? language)
    {
        var fr = language != "en";
        return findings
            .Select((f, index) => (Finding: f, Index: index, Key: f.RuleKey ?? f.Label))
            .GroupBy(x => (x.Key, x.Finding.ClauseReference))
            .Select(g =>
            {
                var hits = g.Select(x => x.Finding).ToList();
                var first = hits[0];
                var ruleKey = g.Key.Key;
                var severity = hits.OrderBy(h => ReviewGrading.Rank(h.Severity)).First().Severity;
                var approval = playbook.Approvals?.GetValueOrDefault(ruleKey);
                // The approver is shown only for the version that produced the finding.
                var approvalMatches = approval is not null && approval.Version == first.RuleVersion;

                var articles = playbook.Rules.TryGetValue(ruleKey, out var linked)
                    ? linked.Select(a => new ReviewArticle(
                        a.ArticleId,
                        ShortCitation(a.Citation),
                        Quote(a.Text, ShortQuoteChars),
                        a.Why,
                        a.Explanations?.GetValueOrDefault(fr ? "fr" : "en"))).ToList()
                    : [];

                string? modelClause = null;
                if (playbook.ModelClauses is not null && playbook.ModelClauses.TryGetValue(ruleKey, out var clause))
                    modelClause = !fr && !string.IsNullOrWhiteSpace(clause.En) ? clause.En : clause.Fr;

                var finding = new ContractFinding(
                    ruleKey,
                    first.ClauseReference,
                    ReviewGrading.IsSeverity(severity) ? severity : ReviewGrading.Medium,
                    first.Label,
                    first.Explanation,
                    first.RecommendedAction,
                    modelClause,
                    articles,
                    first.RuleVersion,
                    approvalMatches ? approval!.ApprovedBy : null,
                    approvalMatches ? approval!.ApprovedAt : null,
                    hits.Select(h => h.Quote).Where(q => !string.IsNullOrWhiteSpace(q)).Select(q => q!).Distinct().ToList(),
                    hits.Select(h => h.Detection).Where(d => d is not null).OrderBy(DetectionRank).FirstOrDefault(),
                    hits.Max(h => h.Confidence));
                return (Finding: finding, FirstIndex: g.Min(x => x.Index));
            })
            .OrderBy(x => ReviewGrading.Rank(x.Finding.Severity))
            .ThenBy(x => x.FirstIndex)
            .Select(x => x.Finding)
            .ToList();
    }

    /// <summary>A check in code is certain, a lawyer's phrase nearly so, a meaning match is a likeness.</summary>
    private static int DetectionRank(string? detection) => detection switch
    {
        "CodeCheck" => 0,
        "Phrase" => 1,
        _ => 2
    };

    /// <summary>"Code des obligations et des contrats (Décret du 15 décembre 1906), art. 244" → "Code des obligations et des contrats, art. 244".</summary>
    public static string ShortCitation(string citation) => Regex.Replace(citation, @"\s*\([^)]*\)", string.Empty);

    /// <summary>
    /// The article word for word; a long one is cut at a sentence end and marked with an ellipsis. A leading amendment
    /// note ("(Modifié par la loi … ) .-") is dropped from the excerpt: it is part of the print, not of the rule.
    /// </summary>
    public static string Quote(string text, int maxChars = MaxQuoteChars)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var amendment = Regex.Match(flat, @"^\((?:Modifié|Ajouté|Abrogé)[^)]*\)\s*\.?\s*-\s*");
        var prefix = string.Empty;
        if (amendment.Success)
        {
            flat = flat[amendment.Length..];
            prefix = "[…] ";
        }
        if (flat.Length <= maxChars) return prefix + flat;
        var cut = flat.LastIndexOf(". ", maxChars, StringComparison.Ordinal);
        return prefix + (cut > maxChars / 3 ? flat[..(cut + 1)] : flat[..maxChars]) + " […]";
    }
}
