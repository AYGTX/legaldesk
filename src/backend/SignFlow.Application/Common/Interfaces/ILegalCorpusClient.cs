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
}

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

public record PlaybookArticle(string ArticleId, string Citation, string Text, string? Why);

public record CitationCheckRequest(string ArticleId, string Quote);

public record CitationCheckResult(bool Found, bool IsVerified, string? Citation, string? Reason);
