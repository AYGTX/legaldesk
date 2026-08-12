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
}

/// <summary>Minimal regulatory-text data the compliance scan needs — not the full registry entity.</summary>
public record RegulatoryTextSummary(Guid Id, string Title, string Keywords, string? Reference);

/// <summary>One regulation's compliance status against a contract: "Addressed", "Gap" or "Unclear".</summary>
public record ComplianceFinding(Guid RegulationId, string RegulationTitle, string Status, string Explanation);

/// <summary>
/// Result of a compliance scan against the published regulatory watch registry. ComplianceScore is
/// the percentage of checked regulations found "Addressed" in the contract text.
/// </summary>
public record ComplianceAnalysis(int ComplianceScore, List<ComplianceFinding> Findings, DateTime AnalyzedAt);

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
    string? ContractReference = null);

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
    List<string>? Limitations = null);

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
