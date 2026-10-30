namespace SignFlow.Application.Common.Interfaces;

public interface IAiService
{
    Task<string> SummarizeDocumentAsync(string text, string title, CancellationToken ct = default);
    Task<GeneratedHtmlTemplate> GenerateHtmlFromTextAsync(string userText, CancellationToken ct = default);
    /// <summary>Fast, offline keyword scan. Runs at upload time so the risk card is never empty.</summary>
    Task<ContractRiskAnalysis> AnalyzeContractRiskAsync(string text, string title, CancellationToken ct = default);
    Task<ObligationSuggestion> SuggestObligationAsync(string text, string title, DateTime referenceDate, CancellationToken ct = default);

    /// <summary>
    /// Full legal review by the language model — identifies the contract, flags missing/weak/ambiguous
    /// clauses with proposed wording, and extracts the renewal/expiry obligation in the same pass.
    /// Slow (tens of seconds), so this is invoked on demand rather than at upload.
    /// </summary>
    Task<ContractAnalysisResult> AnalyzeContractAsync(string text, string title, DateTime referenceDate, CancellationToken ct = default);

    /// <summary>Fast, offline keyword match of a contract against the published regulatory watch registry.</summary>
    Task<ComplianceAnalysis> AnalyzeContractComplianceAsync(
        string text, string title, IReadOnlyList<RegulatoryTextSummary> activeRegulations, CancellationToken ct = default);

    /// <summary>
    /// Full compliance review by the language model — judges whether the contract substantively
    /// satisfies each published regulatory text rather than whether its keywords appear, and proposes
    /// the wording that closes each gap. Slow (tens of seconds), so this is invoked on demand.
    /// </summary>
    /// <param name="legalArticles">Verified articles from the legal corpus. When supplied, the model may only cite
    /// these, and each citation is checked against the corpus afterwards.</param>
    Task<ComplianceAnalysis> ReviewComplianceAsync(
        string text, string title, IReadOnlyList<RegulatoryTextSummary> activeRegulations,
        IReadOnlyList<LegalArticleContext>? legalArticles = null, CancellationToken ct = default);

    /// <summary>
    /// Answers one question about one contract, using only that contract's text. The model must back its answer with
    /// verbatim excerpts; the caller checks them against the text. Needs a configured language model — with none,
    /// throws <see cref="AiNotConfiguredException"/> rather than guessing.
    /// </summary>
    Task<ContractAnswer> AnswerQuestionAsync(
        string text, string title, IReadOnlyList<ChatTurn> history, string question, string language,
        CancellationToken ct = default);

    /// <summary>The contract's key data (parties, dates, renewal, notice, amount, law), each with the sentence it comes
    /// from. Throws <see cref="AiNotConfiguredException"/> when no model is configured.</summary>
    Task<IReadOnlyList<ExtractedTerm>> ExtractKeyTermsAsync(string text, string title, string language, CancellationToken ct = default) =>
        throw new AiNotConfiguredException();

    /// <summary>A short note to decide what to do at the contract's next deadline: renew, renegotiate or terminate.</summary>
    Task<RenewalNote> WriteRenewalNoteAsync(string text, string facts, string language, CancellationToken ct = default) =>
        throw new AiNotConfiguredException();
}

/// <param name="Field">parties | start_date | end_date | duration | auto_renewal | notice_period_days | amount |
/// payment_terms | governing_law | jurisdiction</param>
public record ExtractedTerm(string Field, string Value, string? Quote);

/// <param name="Recommendation">Renew | Renegotiate | Terminate | Review</param>
public record RenewalNote(string Recommendation, string Summary, IReadOnlyList<string> Reasons, IReadOnlyList<string> Quotes);

/// <summary>One earlier message of the conversation. Role is "user" or "assistant".</summary>
public record ChatTurn(string Role, string Content);

/// <summary>The model's answer and the excerpts it says support it (not yet verified).</summary>
public record ContractAnswer(string Answer, IReadOnlyList<string> Quotes);

/// <summary>No language model is configured (Ai:Provider = None), so a conversational feature cannot run.</summary>
public class AiNotConfiguredException() : Exception("No language model is configured.");

/// <summary>The model answered, but not with something usable (cut off, or not the JSON asked for), even after a retry.</summary>
public class AiResponseUnusableException(string message) : Exception(message);

/// <summary>One verified article handed to the model as the law it may rely on.</summary>
public record LegalArticleContext(string ArticleId, string Citation, string Text, double Score = 0);

/// <summary>One clause of the contract and the verified articles of law found for it — by search, with no model.</summary>
public record ClauseLawMatch(string Clause, List<LegalArticleContext> Articles);

/// <summary>Minimal regulatory-text data the compliance scan needs — not the full registry entity.</summary>
public record RegulatoryTextSummary(Guid Id, string Title, string Keywords, string? Reference);

/// <summary>
/// One regulation's compliance status against a contract: "Addressed", "Gap" or "Unclear".
/// Everything past <paramref name="Explanation"/> is only populated by the full model review — the
/// keyword scan and analyses persisted before it existed leave them null.
/// </summary>
public record ComplianceFinding(
    Guid RegulationId,
    string RegulationTitle,
    string Status,
    string Explanation,
    string? Severity = null,
    string? WhyItMatters = null,
    string? RecommendedAction = null,
    string? ProposedLanguage = null,
    string? ContractReference = null,
    string? LegalArticleId = null,
    string? LegalCitation = null,
    string? LegalQuote = null,
    string? CitationStatus = null);

/// <summary>
/// Result of a compliance scan against the published regulatory watch registry. ComplianceScore is
/// the percentage of checked regulations found "Addressed" in the contract text. Limitations records
/// what the review could not establish, so the gap is visible rather than silently guessed at.
/// </summary>
public record ComplianceAnalysis(
    int ComplianceScore,
    List<ComplianceFinding> Findings,
    DateTime AnalyzedAt,
    List<string>? Limitations = null,
    List<ClauseLawMatch>? RelevantLaw = null);

/// <summary>
/// One finding in a <see cref="ContractRiskAnalysis"/>. Severity is "Critical", "High", "Medium" or "Low";
/// FindingType is "MissingClause", "WeakClause", "Ambiguity", "Contradiction" or "Imbalance".
/// Everything past <paramref name="Explanation"/> is only populated by the full model review — the
/// keyword scan and analyses persisted before it existed leave them null.
/// </summary>
public record RiskFinding(
    string ClauseType,
    string Severity,
    string Explanation,
    string? FindingType = null,
    string? WhyItMatters = null,
    string? RecommendedAction = null,
    string? ProposedLanguage = null,
    string? ContractReference = null,
    string? RuleKey = null,
    // Harvey's origin of the finding (rework spec R-M11-5): approved rule version, CodeCheck / MeaningMatch / Phrase,
    // similarity of a meaning match, and the contract's words that raised it.
    int? RuleVersion = null,
    string? Detection = null,
    double? Confidence = null,
    string? Quote = null);

/// <summary>What the analyser could determine about the contract itself. Null where the text does not say.</summary>
public record ContractProfile(
    string? ContractType,
    string? Parties,
    string? Purpose,
    string? Jurisdiction,
    string? GoverningLaw,
    string? Term);

/// <summary>
/// Result of a contract risk scan. RiskScore is 0-100 (higher = riskier). Limitations records what
/// the analysis could not establish — e.g. an unstated governing law — so the gap is visible rather
/// than silently guessed at.
/// </summary>
public record ContractRiskAnalysis(
    int RiskScore,
    List<RiskFinding> Findings,
    DateTime AnalyzedAt,
    ContractProfile? Profile = null,
    List<string>? Limitations = null,
    bool ReviewFailed = false);

/// <summary>Both halves of a full review — one model call answers the risk and the obligation questions.</summary>
public record ContractAnalysisResult(ContractRiskAnalysis Risk, ObligationSuggestion Obligation);

/// <summary>AI-suggested obligation (renewal/expiry/review) date extracted or inferred from the contract text.</summary>
public record ObligationSuggestion(DateTime? DueDate, string? ObligationType, string? Rationale);

public record GeneratedHtmlTemplateField(
    string FieldKey,
    string Label,
    string Type,   // Text | Textarea | Date | Number | Checkbox | Signature
    string Role,   // Initiator | Approver
    bool IsRequired,
    int Order,
    string? AutoFillFrom);

public record GeneratedHtmlTemplate(
    string HtmlContent,
    string SuggestedName,
    List<GeneratedHtmlTemplateField> Fields);
