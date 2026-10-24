namespace SignFlow.Application.Common.Interfaces;

/// <summary>The legal corpus service: verified, article-level law and word-for-word quote checks.</summary>
public interface ILegalCorpusClient
{
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

    /// <summary>
    /// Harvey's contract review: the risks in <paramref name="clauses"/>, found by meaning and by checks in code. Runs
    /// on Harvey's server (the client's own); throws when Harvey cannot be reached or has no semantic model.
    /// </summary>
    Task<IReadOnlyList<RiskFinding>> ReviewAsync(IReadOnlyList<ClauseText> clauses, string? language, CancellationToken ct = default);

    /// <summary>
    /// Finds the fields of a contract PDF and proposes a type, label, fill role and auto-fill for each. Coordinates are
    /// already in the PDF-template model (top-left origin, pixels at 1.5x). Nothing is saved; a person reviews them.
    /// </summary>
    Task<DetectedFormFields> DetectFormFieldsAsync(Stream pdf, string fileName, CancellationToken ct = default);
}

public record DetectedFormField(
    string FieldKey, string Label, string Type, string FillRole, int Page, float X, float Y, float Width, float Height,
    bool IsRequired, string? AutoFillFrom, IReadOnlyList<string> Options, string Source, string Confidence);

/// <param name="Model">Null when the vision model is not installed: only fields already fillable in the PDF are found.</param>
public record DetectedFormFields(int Pages, string? Model, bool AiUsed, IReadOnlyList<DetectedFormField> Fields);

public record ClauseText(string Reference, string Text);

public record ClauseMatch(string Reference, string RuleKey, string Label, double Score, string Sentence, string Example);

public record ClauseClassification(bool Enabled, IReadOnlyList<ClauseMatch> Matches);

/// <summary>Confirmed clause playbook: rule key → verified articles; regulation reference fragment → rule key.</summary>
public record LegalPlaybook(
    IReadOnlyDictionary<string, IReadOnlyList<PlaybookArticle>> Rules,
    IReadOnlyDictionary<string, string> Regulations,
    IReadOnlyDictionary<string, PlaybookWording>? Wording = null,
    IReadOnlyDictionary<string, ModelClause>? ModelClauses = null);

/// <summary>The corrected wording a lawyer approved for a risk, proposed in findings of that risk.</summary>
public record ModelClause(string Fr, string? En);

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
