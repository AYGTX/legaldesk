using System.Text;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Common.Services;

public interface ILegalPlaybookService
{
    /// <summary>Adds findings for lawyer-added wording found in <paramref name="contractText"/>, then attaches the law.</summary>
    Task<ContractRiskAnalysis> AttachToRiskAsync(ContractRiskAnalysis risk, string? contractText, CancellationToken ct = default);

    Task<ComplianceAnalysis> AttachToComplianceAsync(
        ComplianceAnalysis compliance, IReadOnlyList<RegulatoryTextSummary> regulations, CancellationToken ct = default);
}

/// <summary>
/// Gives rule-scan findings their legal basis without any model: a lawyer-confirmed playbook links each detected risk
/// to verified articles, and the findings quote those articles word for word. A finding that already carries a legal
/// basis (from a model review) keeps its own.
/// </summary>
public class LegalPlaybookService(ILegalCorpusClient corpus, ILogger<LegalPlaybookService> logger) : ILegalPlaybookService
{
    private const int MaxQuoteChars = 700;

    private LegalPlaybook? _playbook;

    private async Task<LegalPlaybook?> LoadAsync(CancellationToken ct)
    {
        if (!corpus.IsEnabled) return null;
        if (_playbook is not null) return _playbook;
        try
        {
            return _playbook = await corpus.GetPlaybookAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Legal playbook unavailable — findings keep no legal basis");
            return null;
        }
    }

    public async Task<ContractRiskAnalysis> AttachToRiskAsync(ContractRiskAnalysis risk, string? contractText, CancellationToken ct = default)
    {
        var playbook = await LoadAsync(ct);
        if (playbook is null) return risk;
        if (playbook.Wording is { Count: > 0 })
            risk = ContractRuleEngine.ApplyPlaybookWording(risk, contractText, playbook.Wording);
        risk = await AddPossibleRisksAsync(risk, contractText, ct);
        return AttachToRisk(risk, playbook);
    }

    /// <summary>Meaning-based leads from the corpus service; a failure there costs the leads, never the review.</summary>
    private async Task<ContractRiskAnalysis> AddPossibleRisksAsync(ContractRiskAnalysis risk, string? contractText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contractText)) return risk;
        try
        {
            var clauses = ContractRuleEngine.ClauseTexts(contractText);
            if (clauses.Count == 0) return risk;
            var result = await corpus.ClassifyAsync(clauses, ct);
            return result.Enabled ? ContractRuleEngine.ApplyPossibleMatches(risk, contractText, result.Matches) : risk;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Meaning-based clause matching unavailable — review continues without possible risks");
            return risk;
        }
    }

    public async Task<ComplianceAnalysis> AttachToComplianceAsync(
        ComplianceAnalysis compliance, IReadOnlyList<RegulatoryTextSummary> regulations, CancellationToken ct = default)
    {
        var playbook = await LoadAsync(ct);
        return playbook is null ? compliance : AttachToCompliance(compliance, regulations, playbook);
    }

    public static ContractRiskAnalysis AttachToRisk(ContractRiskAnalysis risk, LegalPlaybook playbook) =>
        risk with
        {
            Findings = risk.Findings
                .Select(f => f.RuleKey is not null && f.WhyItMatters is null
                             && playbook.Rules.TryGetValue(f.RuleKey, out var articles) && articles.Count > 0
                    ? f with { WhyItMatters = Format(articles) }
                    : f)
                .ToList()
        };

    public static ComplianceAnalysis AttachToCompliance(
        ComplianceAnalysis compliance, IReadOnlyList<RegulatoryTextSummary> regulations, LegalPlaybook playbook)
    {
        var byId = regulations.ToDictionary(r => r.Id);

        return compliance with
        {
            Findings = compliance.Findings.Select(f =>
            {
                if (f.LegalArticleId is not null || !byId.TryGetValue(f.RegulationId, out var regulation)) return f;

                var ruleKey = playbook.Regulations
                    .Where(m => Contains(regulation.Reference, m.Key) || Contains(regulation.Title, m.Key))
                    .Select(m => m.Value)
                    .FirstOrDefault();
                if (ruleKey is null || !playbook.Rules.TryGetValue(ruleKey, out var articles) || articles.Count == 0) return f;

                var first = articles[0];
                return f with
                {
                    WhyItMatters = f.WhyItMatters ?? Format(articles),
                    LegalArticleId = first.ArticleId,
                    LegalCitation = first.Citation,
                    LegalQuote = Quote(first.Text),
                    // The quote is the article's own text from verified law, so there is nothing left to check.
                    CitationStatus = LegalCitationGuard.Verified
                };
            }).ToList()
        };
    }

    /// <summary>One block per article: short citation, then the quote, then why it applies — separated by blank lines.</summary>
    public static string Format(IReadOnlyList<PlaybookArticle> articles)
    {
        var sb = new StringBuilder();
        foreach (var article in articles)
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(ShortCitation(article.Citation)).Append(" : « ").Append(Quote(article.Text, ShortQuoteChars)).Append(" »");
            if (!string.IsNullOrWhiteSpace(article.Why)) sb.Append("\n→ ").Append(article.Why);
        }
        return sb.ToString();
    }

    private const int ShortQuoteChars = 320;

    /// <summary>"Code des obligations et des contrats (Décret du 15 décembre 1906), art. 244" → "Code des obligations et des contrats, art. 244".</summary>
    public static string ShortCitation(string citation) =>
        System.Text.RegularExpressions.Regex.Replace(citation, @"\s*\([^)]*\)", string.Empty);

    /// <summary>
    /// The article word for word; a long one is cut at a sentence end and marked with an ellipsis. A leading amendment
    /// note ("(Modifié par la loi … ) .-") is dropped from the excerpt: it is part of the print, not of the rule.
    /// </summary>
    public static string Quote(string text, int maxChars = MaxQuoteChars)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var amendment = System.Text.RegularExpressions.Regex.Match(flat, @"^\((?:Modifié|Ajouté|Abrogé)[^)]*\)\s*\.?\s*-\s*");
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

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
