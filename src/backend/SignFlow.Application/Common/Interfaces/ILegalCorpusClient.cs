namespace SignFlow.Application.Common.Interfaces;

/// <summary>The legal corpus service: verified, article-level law and word-for-word quote checks.</summary>
public interface ILegalCorpusClient
{
    bool IsEnabled { get; }

    /// <summary>The most relevant verified, published articles for <paramref name="query"/>.</summary>
    Task<IReadOnlyList<LegalArticleContext>> SearchAsync(string query, int topK, CancellationToken ct = default);

    Task<LegalPlaybook> GetPlaybookAsync(CancellationToken ct = default);

    /// <summary>One result per request, in order.</summary>
    Task<IReadOnlyList<CitationCheckResult>> CheckCitationsAsync(
        IReadOnlyList<CitationCheckRequest> citations, CancellationToken ct = default);

    /// <summary>
    /// Matches clauses to confirmed playbook rules by meaning, against the rules' example clauses. Runs inside the
    /// corpus service on the client's servers; returns Enabled = false when no semantic model is installed there.
    /// </summary>
    Task<ClauseClassification> ClassifyAsync(IReadOnlyList<ClauseText> clauses, CancellationToken ct = default);
}

public record ClauseText(string Reference, string Text);

public record ClauseMatch(string Reference, string RuleKey, string Label, double Score, string Sentence, string Example);

public record ClauseClassification(bool Enabled, IReadOnlyList<ClauseMatch> Matches);

/// <summary>Confirmed clause playbook: rule key → verified articles; regulation reference fragment → rule key.</summary>
public record LegalPlaybook(
    IReadOnlyDictionary<string, IReadOnlyList<PlaybookArticle>> Rules,
    IReadOnlyDictionary<string, string> Regulations,
    IReadOnlyDictionary<string, PlaybookWording>? Wording = null);

/// <summary>
/// Extra wording a lawyer attached to a confirmed rule: a clause containing any of these phrases is flagged for that
/// rule, even when the built-in detection misses it.
/// </summary>
public record PlaybookWording(string Label, IReadOnlyList<string> Phrases);

/// <summary>An article a playbook rule quotes; <paramref name="Explanations"/> holds lawyer-approved plain-language
/// explanations of its current wording, by language ("fr", "en").</summary>
public record PlaybookArticle(string ArticleId, string Citation, string Text, string? Why,
    IReadOnlyDictionary<string, string>? Explanations = null);

public record CitationCheckRequest(string ArticleId, string Quote);

public record CitationCheckResult(bool Found, bool IsVerified, string? Citation, string? Reason);
