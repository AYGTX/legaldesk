namespace SignFlow.Application.Common.Interfaces;

public interface IAiService
{
    Task<string> SummarizeDocumentAsync(string text, string title, CancellationToken ct = default);
    Task<GeneratedHtmlTemplate> GenerateHtmlFromTextAsync(string userText, CancellationToken ct = default);
    Task<ObligationSuggestion> SuggestObligationAsync(string text, string title, DateTime referenceDate, CancellationToken ct = default);

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
