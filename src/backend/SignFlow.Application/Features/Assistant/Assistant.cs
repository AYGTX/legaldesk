using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Assistant;

// ------------------------------------------------------------------ summary

public record SummaryPointDto(string Clause, string Point, string Quote);

/// <summary>The assistant's summary of one version (R-M12-3, R-M12-4): only points whose quote was found word for word,
/// the clauses a limit kept from being read, and the label naming the provider, model and prompt.</summary>
public record ContractSummaryDto(int Version, List<SummaryPointDto> Points, List<string> NotRead, int Dropped, ModelLabelDto Label);

public record GetContractSummaryQuery(Guid DocumentId) : IQuery<ContractSummaryDto>;

/// <summary>The summary as a participant sees it on the signing page: only when the owner shared it (R-M12-8).</summary>
public record GetSigningSummaryQuery(Guid RecipientId) : IQuery<ContractSummaryDto>;

internal sealed record StoredSummary(List<SummaryPointDto> Points, List<string> NotRead, int Dropped);

/// <summary>Reads the stored summary of the current version, or writes it once (and records that the text was sent).</summary>
public class ContractSummaries(IApplicationDbContext db, IContractTextProvider texts, IAiService ai, ModelAccess model)
{
    public async Task<ContractSummaryDto> GetOrCreateAsync(Document document, Guid? userId, string? actorEmail, Guid? recipientId, string? actorName,
        CancellationToken ct)
    {
        await model.RequireOnAsync(ct);
        var stored = await db.ModelOutputs.AsNoTracking()
            .Where(o => o.DocumentId == document.Id && o.Feature == "summary" && o.VersionNumber == document.Version)
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(ct);
        if (stored is not null) return ToDto(stored);

        await model.BeginAsync(document.Id, userId, recipientId, ct);
        var text = await texts.GetTextAsync(document, ct);
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("This contract has no readable text to summarise (a scan without OCR?).");
        var language = ContractLanguage.ForAnswer(text, "fr");

        var clauses = ClauseSplitter.ClauseTexts(text);
        var result = await ai.SummarizeClausesAsync(clauses, document.Title, language, ct);
        await model.RecordAsync(document.Id, "summary", text.Length, userId, actorEmail, recipientId, actorName, ct);

        // A point whose quote is not in the contract is not shown (R-M12-3).
        var kept = result.Points
            .Where(p => p.Quote is { Length: > 0 } q && QuoteMatcher.IsVerbatim(text, q))
            .Select(p => new SummaryPointDto(p.Clause, p.Point, p.Quote!.Trim()))
            .ToList();
        var content = new StoredSummary(kept, [.. result.NotRead], result.Points.Count - kept.Count);

        var output = ModelOutput.Create(document.Id, document.Version, "summary", model.Info.Provider, model.Info.Model,
            PromptVersions.Summary, JsonSerializer.Serialize(content), verified: true);
        db.ModelOutputs.Add(output);
        await db.SaveChangesAsync(ct);
        return ToDto(output);
    }

    private static ContractSummaryDto ToDto(ModelOutput output)
    {
        var content = JsonSerializer.Deserialize<StoredSummary>(output.ContentJson) ?? new StoredSummary([], [], 0);
        return new ContractSummaryDto(output.VersionNumber, content.Points, content.NotRead, content.Dropped,
            new ModelLabelDto(output.Provider, output.Model, output.PromptVersion, output.CreatedAt));
    }
}

public class GetContractSummaryQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser, ContractSummaries summaries)
    : IQueryHandler<GetContractSummaryQuery, ContractSummaryDto>
{
    public async Task<ContractSummaryDto> HandleAsync(GetContractSummaryQuery query, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == query.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), query.DocumentId);
        if (!await DocumentReadAccess.CanReadAsync(db, currentUser, document.Id, cancellationToken))
            throw new ForbiddenAccessException();
        return await summaries.GetOrCreateAsync(document, currentUser.UserId, currentUser.UserEmail, null, null, cancellationToken);
    }
}

public class GetSigningSummaryQueryHandler(IApplicationDbContext db, ContractSummaries summaries) : IQueryHandler<GetSigningSummaryQuery, ContractSummaryDto>
{
    public async Task<ContractSummaryDto> HandleAsync(GetSigningSummaryQuery query, CancellationToken cancellationToken = default)
    {
        var recipient = await db.Recipients.Include(r => r.Document).FirstOrDefaultAsync(r => r.Id == query.RecipientId, cancellationToken)
            ?? throw new NotFoundException("Signing session not found.");
        if (!recipient.Document.ShareSummaryWithParticipants)
            throw NotFoundException.WithCode("summary.notShared", "The sender did not share the assistant's summary.");
        // The participant is the one the text was summarised for: named in the audit (R-M12-2).
        return await summaries.GetOrCreateAsync(recipient.Document, null, recipient.Email, recipient.Id, recipient.Name, cancellationToken);
    }
}

// ------------------------------------------------------------------ negotiation explainer

/// <param name="PreviousQuote">The previous wording, as it is in that version (verified by construction: it is the clause).</param>
public record ChangeExplanationDto(string Change, string? PreviousQuote, string? CurrentQuote, string Explanation);

public record ExplainChangesDto(int From, int To, List<ChangeExplanationDto> Changes, int Unexplained, ModelLabelDto Label);

/// <summary>What changed between two versions, in plain words (R-M12-7); the review rates risk, this never does.</summary>
public record ExplainChangesCommand(Guid DocumentId, int From, int To) : ICommand<ExplainChangesDto>;

public class ExplainChangesCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IStorageService storage,
    IPdfService pdf,
    IAiService ai,
    ModelAccess model) : ICommandHandler<ExplainChangesCommand, ExplainChangesDto>
{
    private const int MaxChanges = 30;

    public async Task<ExplainChangesDto> HandleAsync(ExplainChangesCommand command, CancellationToken cancellationToken = default)
    {
        if (command.From == command.To || command.From < 1 || command.To < 1)
            throw new ValidationException("Choose two different versions.");
        var document = await db.Documents.Include(d => d.Versions).FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);
        if (!await DocumentReadAccess.CanReadAsync(db, currentUser, document.Id, cancellationToken))
            throw new ForbiddenAccessException();

        var key = $"{command.From}->{command.To}";
        var stored = await db.ModelOutputs.AsNoTracking()
            .FirstOrDefaultAsync(o => o.DocumentId == document.Id && o.Feature == "explainer" && o.Key == key, cancellationToken);
        if (stored is null)
        {
            await model.BeginAsync(document.Id, currentUser.UserId, null, cancellationToken);
            var before = await TextOfAsync(document, command.From, cancellationToken);
            var after = await TextOfAsync(document, command.To, cancellationToken);
            var diff = ContractDiffer.Compare(before, after);
            var changed = diff.Clauses.Where(c => c.Change != ClauseChangeType.Unchanged).ToList();
            var toExplain = changed.Take(MaxChanges).Select((c, i) => new ChangeToExplain(i, c.PreviousText, c.CurrentText)).ToList();
            var language = ContractLanguage.ForAnswer(after, "fr");

            var explained = await ai.ExplainChangesAsync(toExplain, language, cancellationToken);
            await model.RecordAsync(document.Id, "explainer",
                toExplain.Sum(c => (c.Previous?.Length ?? 0) + (c.Current?.Length ?? 0)), currentUser.UserId, currentUser.UserEmail, ct: cancellationToken);

            // Each change shows the two wordings exactly as they are in each version, checked against that version's text.
            var items = toExplain.Select(c => (Change: changed[c.Index], Text: explained.FirstOrDefault(e => e.Index == c.Index)?.Explanation))
                .Where(x => x.Text is not null)
                .Select(x => new ChangeExplanationDto(x.Change.Change.ToString(),
                    x.Change.PreviousText is { } p && QuoteMatcher.IsVerbatim(before, p) ? p : null,
                    x.Change.CurrentText is { } n && QuoteMatcher.IsVerbatim(after, n) ? n : null,
                    x.Text!))
                .Where(x => x.PreviousQuote is not null || x.CurrentQuote is not null)
                .ToList();
            var content = new { Changes = items, Unexplained = changed.Count - items.Count };
            stored = ModelOutput.Create(document.Id, command.To, "explainer", model.Info.Provider, model.Info.Model, PromptVersions.Explainer,
                JsonSerializer.Serialize(content), verified: true, key);
            db.ModelOutputs.Add(stored);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await model.RequireOnAsync(cancellationToken);
        }

        using var json = JsonDocument.Parse(stored.ContentJson);
        var list = JsonSerializer.Deserialize<List<ChangeExplanationDto>>(json.RootElement.GetProperty("Changes").GetRawText()) ?? [];
        return new ExplainChangesDto(command.From, command.To, list, json.RootElement.GetProperty("Unexplained").GetInt32(),
            new ModelLabelDto(stored.Provider, stored.Model, stored.PromptVersion, stored.CreatedAt));
    }

    private async Task<string> TextOfAsync(Document document, int version, CancellationToken ct)
    {
        var path = version == document.Version ? document.StoragePath
            : document.Versions.FirstOrDefault(v => v.VersionNumber == version)?.StoragePath
              ?? throw new NotFoundException(nameof(DocumentVersion), version);
        await using var file = await storage.DownloadAsync(path, ct);
        return await pdf.ExtractTextAsync(file, ct);
    }
}
