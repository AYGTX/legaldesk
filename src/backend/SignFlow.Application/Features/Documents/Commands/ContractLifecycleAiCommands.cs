using SignFlow.Application.Common.Security;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

// ------------------------------------------------------------------ key data

/// <summary>Reads the contract's key data with the language model. Nothing is saved: the person reviews the values and
/// applies the term with the usual term form.</summary>
public record ExtractKeyTermsCommand(Guid DocumentId, string Language) : ICommand<KeyTermsDto>;

/// <param name="Verified">True when <paramref name="Quote"/> was found word for word in the contract.</param>
public record KeyTermDto(string Field, string Value, string? Quote, bool Verified);

/// <summary>The term fields LegalDesk tracks, read from the verified values; null when the contract does not state them.</summary>
public record SuggestedTermDto(DateTime? StartDate, DateTime? EndDate, bool? AutoRenews, int? NoticePeriodDays);

public record KeyTermsDto(IReadOnlyList<KeyTermDto> Terms, SuggestedTermDto Suggested, string Language);

public class ExtractKeyTermsCommandHandler(
    IApplicationDbContext db,
    IContractTextProvider textProvider,
    IAiService aiService,
    ICurrentUserService currentUser,
    IAuditService auditService)
    : ICommandHandler<ExtractKeyTermsCommand, KeyTermsDto>
{
    public async Task<KeyTermsDto> HandleAsync(ExtractKeyTermsCommand command, CancellationToken cancellationToken = default)
    {
        var (document, text) = await ContractAccess.LoadAsync(db, textProvider, currentUser, command.DocumentId, cancellationToken);
        var language = ContractLanguage.ForAnswer(text, command.Language);

        var terms = await ReadAsync(aiService, text, document.Title, language, cancellationToken);

        await auditService.LogAsync(document.Id, AuditAction.ContractKeyTermsExtracted, "system",
            $"Key data read by Harvey: {terms.Count} value(s) with a verified excerpt",
            currentUser.UserId, actorEmail: currentUser.UserEmail, cancellationToken: cancellationToken);

        return new KeyTermsDto(terms, Suggest(terms), language);
    }

    /// <summary>The key data with a verified excerpt: a value the model cannot point to in the contract, word for word,
    /// is dropped rather than presented as the contract's.</summary>
    public static async Task<List<KeyTermDto>> ReadAsync(IAiService aiService, string text, string title, string language, CancellationToken ct)
    {
        var extracted = await aiService.ExtractKeyTermsAsync(text, title, language, ct);
        return extracted
            .Where(t => !string.IsNullOrWhiteSpace(t.Field) && !string.IsNullOrWhiteSpace(t.Value))
            .Select(t => new KeyTermDto(t.Field.Trim(), t.Value.Trim(), t.Quote?.Trim(),
                !string.IsNullOrWhiteSpace(t.Quote) && QuoteMatcher.IsVerbatim(text, t.Quote)))
            .Where(t => t.Verified)
            .GroupBy(t => t.Field)
            .Select(g => g.First())
            .ToList();
    }

    public static SuggestedTermDto Suggest(IReadOnlyList<KeyTermDto> terms)
    {
        string? Get(string field) => terms.FirstOrDefault(t => t.Field == field)?.Value;
        static DateTime? Date(string? v) =>
            DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
                ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc) : null;
        var renew = Get("auto_renewal")?.ToLowerInvariant();
        return new SuggestedTermDto(
            Date(Get("start_date")),
            Date(Get("end_date")),
            renew is "true" or "oui" or "yes" ? true : renew is "false" or "non" or "no" ? false : null,
            int.TryParse(Get("notice_period_days"), out var days) && days is > 0 and < 3650 ? days : null);
    }
}

// ------------------------------------------------------------------ renewal note

/// <summary>A short decision note for the contract's next deadline. Nothing is saved; an audit entry records it.</summary>
public record WriteRenewalNoteCommand(Guid DocumentId, string Language) : ICommand<RenewalNoteDto>;

public record RenewalNoteDto(
    string Recommendation, string Summary, IReadOnlyList<string> Reasons, IReadOnlyList<ChatCitationDto> Citations,
    LifecycleStage Stage, DateTime? EndDate, DateTime? NoticeDeadline, bool AutoRenews, string Language);

public class WriteRenewalNoteCommandHandler(
    IApplicationDbContext db,
    IContractTextProvider textProvider,
    IAiService aiService,
    ICurrentUserService currentUser,
    IAuditService auditService)
    : ICommandHandler<WriteRenewalNoteCommand, RenewalNoteDto>
{
    private static readonly string[] Recommendations = ["Renew", "Renegotiate", "Terminate", "Review"];

    public async Task<RenewalNoteDto> HandleAsync(WriteRenewalNoteCommand command, CancellationToken cancellationToken = default)
    {
        var (document, text) = await ContractAccess.LoadAsync(db, textProvider, currentUser, command.DocumentId, cancellationToken);
        if (document.ContractEndDate is null)
            throw new ValidationException("Record the contract's end date first: the note is about its next deadline.");
        var language = ContractLanguage.ForAnswer(text, command.Language);
        var status = ContractTermEvaluator.Evaluate(document.ContractStartDate, document.ContractEndDate, document.NoticePeriodDays, DateTime.UtcNow);

        var note = await aiService.WriteRenewalNoteAsync(text, Facts(document, status), language, cancellationToken);
        var citations = note.Quotes.Where(q => !string.IsNullOrWhiteSpace(q)).Select(q => q.Trim()).Distinct().Take(4)
            .Select(q => new ChatCitationDto(q, QuoteMatcher.IsVerbatim(text, q))).Where(c => c.Verified).ToList();
        var recommendation = Recommendations.FirstOrDefault(r => r.Equals(note.Recommendation?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Review";

        await auditService.LogAsync(document.Id, AuditAction.ContractRenewalNoteWritten, "system",
            $"Renewal note written by Harvey: {recommendation}",
            currentUser.UserId, actorEmail: currentUser.UserEmail, cancellationToken: cancellationToken);

        return new RenewalNoteDto(recommendation, note.Summary, note.Reasons.Take(5).ToList(), citations,
            status.Stage, document.ContractEndDate, status.NoticeDeadline, document.AutoRenews, language);
    }

    /// <summary>What LegalDesk knows for certain, given to the model as facts it must not contradict.</summary>
    public static string Facts(Document document, ContractTermStatus status)
    {
        static string D(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "not recorded";
        var sb = new StringBuilder();
        sb.AppendLine($"Today: {D(DateTime.UtcNow)}");
        sb.AppendLine($"Start date: {D(document.ContractStartDate)}; end date: {D(document.ContractEndDate)}");
        sb.AppendLine($"Automatic renewal: {(document.AutoRenews ? "yes" : "no")}; notice period: {(document.NoticePeriodDays is { } n ? $"{n} days" : "not recorded")}");
        sb.AppendLine($"Last day to give notice: {D(status.NoticeDeadline)}; days to end: {status.DaysToEnd?.ToString(CultureInfo.InvariantCulture) ?? "?"}; stage: {status.Stage}");
        if (document.ObligationDueDate is not null)
            sb.AppendLine($"Tracked obligation: {document.ObligationType} due {D(document.ObligationDueDate)}");
        foreach (var risk in OpenRisks(document.AiRiskAnalysisJson))
            sb.AppendLine($"Open risk from the review: {risk}");
        return sb.ToString();
    }

    private static IEnumerable<string> OpenRisks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) yield break;
        List<(string, string)> risks = [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("findings", out var findings) || doc.RootElement.TryGetProperty("Findings", out findings))
                foreach (var f in findings.EnumerateArray())
                {
                    var severity = (f.TryGetProperty("severity", out var s) || f.TryGetProperty("Severity", out s)) ? s.GetString() : null;
                    var type = (f.TryGetProperty("clauseType", out var c) || f.TryGetProperty("ClauseType", out c)) ? c.GetString() : null;
                    if (severity is "High" or "Medium" && type is not null) risks.Add((severity, type));
                }
        }
        catch (JsonException) { }
        foreach (var (severity, type) in risks.Take(8)) yield return $"{type} ({severity})";
    }
}

internal static class ContractAccess
{
    public static async Task<(Document Document, string Text)> LoadAsync(
        IApplicationDbContext db, IContractTextProvider textProvider, ICurrentUserService currentUser, Guid id, CancellationToken ct)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct)
                       ?? throw new NotFoundException(nameof(Document), id);
        if (!currentUser.Can(Permissions.ManageAllContracts) && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();
        var text = await textProvider.GetTextAsync(document, ct);
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("This contract has no readable text (a scan without OCR?).");
        return (document, text);
    }
}
