using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Queries;

/// <summary>
/// Redlines two versions of the same contract against each other — the first question a legal
/// reviewer asks when a counterparty returns a draft.
/// </summary>
public record CompareDocumentVersionsQuery(Guid DocumentId, int FromVersion, int ToVersion)
    : IQuery<VersionComparisonDto>;

public record VersionComparisonDto(
    Guid DocumentId,
    int FromVersion,
    int ToVersion,
    int AddedCount,
    int RemovedCount,
    int ModifiedCount,
    int UnchangedCount,
    IReadOnlyList<ClauseDiff> Clauses,
    int MovedCount = 0);

public class CompareDocumentVersionsQueryHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<CompareDocumentVersionsQueryHandler> logger)
    : IQueryHandler<CompareDocumentVersionsQuery, VersionComparisonDto>
{
    public async Task<VersionComparisonDto> HandleAsync(
        CompareDocumentVersionsQuery query, CancellationToken cancellationToken = default)
    {
        // Guarded here rather than in a FluentValidation validator: validators are registered in DI
        // but no pipeline invokes them, so a rule declared above the handler would never run.
        if (query.FromVersion == query.ToVersion)
            throw new Common.Exceptions.ValidationException("Choose two different versions to compare.");
        if (query.FromVersion < 1 || query.ToVersion < 1)
            throw new Common.Exceptions.ValidationException("Version numbers start at 1.");

        var document = await DocumentVersionAccess.LoadAsync(db, currentUser, query.DocumentId, cancellationToken);

        // HTML-template documents have no owner-managed PDF and therefore no revision chain — the
        // same boundary UploadRevisionCommand enforces.
        if (document.HtmlTemplateId.HasValue)
            throw new Common.Exceptions.ValidationException("Only PDF contracts have comparable versions.");

        var fromPath = DocumentVersionAccess.ResolveStoragePath(document, query.FromVersion);
        var toPath = DocumentVersionAccess.ResolveStoragePath(document, query.ToVersion);

        var fromText = await ExtractAsync(fromPath, cancellationToken);
        var toText = await ExtractAsync(toPath, cancellationToken);

        var diff = ContractDiffer.Compare(fromText, toText);

        // Reading a superseded draft is a consultation of the archive, and the audit trail is meant
        // to answer "who looked at what" — a comparison is two of those in one action.
        await auditService.LogAsync(
            document.Id,
            AuditAction.VersionsCompared,
            "system",
            $"Versions compared (v{query.FromVersion} → v{query.ToVersion}): " +
                $"{diff.AddedCount} added, {diff.RemovedCount} removed, {diff.ModifiedCount} modified, {diff.MovedCount} moved",
            currentUser.UserId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Document {DocumentId} versions v{From}→v{To} compared by user {UserId}: {Added}/{Removed}/{Modified}",
            document.Id, query.FromVersion, query.ToVersion, currentUser.UserId,
            diff.AddedCount, diff.RemovedCount, diff.ModifiedCount);

        return new VersionComparisonDto(
            document.Id,
            query.FromVersion,
            query.ToVersion,
            diff.AddedCount,
            diff.RemovedCount,
            diff.ModifiedCount,
            diff.UnchangedCount,
            diff.Clauses,
            diff.MovedCount);
    }

    private async Task<string> ExtractAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = await storage.DownloadAsync(path, cancellationToken);
        return await pdfService.ExtractTextAsync(stream, cancellationToken);
    }
}
