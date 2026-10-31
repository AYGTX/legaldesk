namespace SignFlow.Application.Common.Interfaces;

/// <summary>The legal corpus service: verified, article-level law and word-for-word quote checks.</summary>
public interface ILegalCorpusClient
{
    /// <summary>Harvey answers its health check. Used before queueing a review, so a review that cannot run is refused at
    /// once (code review.unavailable) instead of failing in the background.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>The rules in service (approved versions only), with their verified articles and approved model clauses.</summary>
    Task<LegalPlaybook> GetPlaybookAsync(CancellationToken ct = default);

    /// <summary>
    /// Harvey's contract review (R-M8-1, R-M8-2): the risks in <paramref name="clauses"/>, found with approved rules only,
    /// minus the rules the organisation switched off and with its severities. Throws when Harvey cannot answer.
    /// </summary>
    Task<HarveyReview> ReviewAsync(
        IReadOnlyList<ClauseText> clauses,
        string? language,
        IReadOnlyCollection<string> disabledRules,
        IReadOnlyDictionary<string, string> severityOverrides,
        CancellationToken ct = default);

    /// <summary>Reports a finding a user marked "not applicable" to the lawyers who keep the grid (R-M8-4). Safe to retry.</summary>
    Task SendFeedbackAsync(ReviewFeedbackReport report, CancellationToken ct = default);

    /// <summary>
    /// Finds the fields of a contract PDF and proposes a type, label, fill role and auto-fill for each. Coordinates are
    /// PDF points from the top-left of the page. Nothing is saved; a person reviews them.
    /// </summary>
    Task<DetectedFormFields> DetectFormFieldsAsync(Stream pdf, string fileName, CancellationToken ct = default);
}

/// <summary>One finding as Harvey returns it: one hit of one rule in one clause.</summary>
/// <param name="Label">The rule's label (Harvey's clause_type).</param>
/// <param name="Detection">CodeCheck, MeaningMatch or Phrase.</param>
/// <param name="Confidence">Similarity of a meaning match.</param>
/// <param name="Quote">The contract's words that raised it.</param>
public record HarveyFinding(
    string? RuleKey,
    string Label,
    string Severity,
    string Explanation,
    string? RecommendedAction,
    string? ClauseReference,
    string? FindingType,
    int? RuleVersion,
    string? Detection,
    double? Confidence,
    string? Quote);

/// <param name="GridVersion">Fingerprint of the rule versions used.</param>
public record HarveyReview(string Language, string? GridVersion, IReadOnlyList<HarveyFinding> Findings);

/// <param name="SourceRef">LegalDesk's id of the finding, so a retried delivery is stored once.</param>
public record ReviewFeedbackReport(string SourceRef, string RuleKey, int? RuleVersion, string? ClauseReference, string? Quote, string Reason);

public record DetectedFormField(
    string FieldKey, string Label, string Type, string FillRole, int Page, float X, float Y, float Width, float Height,
    bool IsRequired, string? AutoFillFrom, IReadOnlyList<string> Options, string Source, string Confidence);

/// <param name="Model">Null when the vision model is not installed: only fields already fillable in the PDF are found.</param>
public record DetectedFormFields(int Pages, string? Model, bool AiUsed, IReadOnlyList<DetectedFormField> Fields);

public record ClauseText(string Reference, string Text);

/// <summary>The rules in service: rule key → verified articles, approved model clause, and who approved the version.</summary>
public record LegalPlaybook(
    IReadOnlyDictionary<string, IReadOnlyList<PlaybookArticle>> Rules,
    IReadOnlyDictionary<string, ModelClause>? ModelClauses = null,
    IReadOnlyDictionary<string, RuleApproval>? Approvals = null);

/// <summary>The approved version of a rule in service, and the lawyer who approved it.</summary>
public record RuleApproval(int? Version, string? ApprovedBy, DateTime? ApprovedAt);

/// <summary>The corrected wording a lawyer approved for a risk, proposed in findings of that risk.</summary>
public record ModelClause(string Fr, string? En);

/// <summary>An article a playbook rule quotes; <paramref name="Explanations"/> holds lawyer-approved plain-language
/// explanations of its current wording, by language ("fr", "en").</summary>
public record PlaybookArticle(string ArticleId, string Citation, string Text, string? Why,
    IReadOnlyDictionary<string, string>? Explanations = null);
