using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>
/// Owner response to a ChangesRequested pause: snapshots the current file as a DocumentVersion,
/// swaps in the revised PDF, voids field completion (Signature rows are kept as evidence) and
/// returns the document to Draft for a full re-approval round. The owner then re-places fields
/// if needed and explicitly re-sends, which rebuilds the workflow from the current recipients.
/// </summary>
public record UploadRevisionCommand(
    Guid DocumentId,
    Stream FileStream,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    string? ChangeNote) : ICommand<UploadRevisionResult>;

public record UploadRevisionResult(Guid DocumentId, int Version, int PageCount);

public class UploadRevisionCommandValidator : AbstractValidator<UploadRevisionCommand>
{
    private const long MaxFileSizeBytes = Common.Services.PdfUploadGuard.MaxBytes; // 50 MB, the same everywhere

    public UploadRevisionCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255)
            .Must(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only PDF files are supported.");
        RuleFor(x => x.FileSizeBytes).GreaterThan(0).LessThanOrEqualTo(MaxFileSizeBytes)
            .WithMessage("File size must be between 1 byte and 50 MB.");
        RuleFor(x => x.ContentType).Must(ct => ct == "application/pdf").WithMessage("Content type must be application/pdf.");
        RuleFor(x => x.ChangeNote).MaximumLength(2000);
    }
}

public class UploadRevisionCommandHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    Common.Services.PdfUploadGuard uploadGuard,
    IReviewQueue queue,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<UploadRevisionCommandHandler> logger) : ICommandHandler<UploadRevisionCommand, UploadRevisionResult>
{
    public async Task<UploadRevisionResult> HandleAsync(UploadRevisionCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.Recipients)
            .Include(d => d.SignatureFields)
            .Include(d => d.Workflow)
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        if (document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        // A draft is revised while its owner prepares it (fixing what the pre-flight review found); a sent contract
        // only after a participant sent it back.
        if (document.Status is not (DocumentStatus.Draft or DocumentStatus.ChangesRequested))
            throw new ConflictException("A revision can only be uploaded to a draft, or after a participant requested changes.");

        // HTML-template documents have no owner-managed PDF: their "revision" would be editing
        // the filled values/snapshot, which is a different mechanic (deferred).
        if (document.HtmlTemplateId.HasValue)
            throw new ConflictException("Revisions are only supported for PDF-based documents.");

        // A PDF-template contract's file and layout belong to the template; its owner revises the answers instead.
        if (document.PdfTemplateId.HasValue)
            throw new ConflictException("This contract was built from a template: revise its answers instead of uploading a file.");

        // Size, %PDF- signature, readable, not encrypted, virus scan (R-M2-1, R-M2-2).
        var pageCount = await uploadGuard.EnsureSafeAsync(command.FileStream, command.FileName, cancellationToken);
        var hash = await pdfService.ComputeHashAsync(command.FileStream, cancellationToken);
        command.FileStream.Position = 0;

        var userId = currentUser.UserId!.Value;

        // Preserve the outgoing file as an immutable version row — the file itself stays in
        // storage untouched; only the document's live pointer moves to the new upload.
        var previousVersion = DocumentVersion.Create(
            document.Id,
            document.Version,
            document.StoragePath,
            document.FileSizeBytes,
            userId,
            command.ChangeNote);
        previousVersion.SetCreatedBy(userId.ToString());
        db.DocumentVersions.Add(previousVersion);

        var storagePath = await storage.UploadAsync(
            command.FileStream, command.FileName, command.ContentType, $"documents/{userId}", cancellationToken);

        // Void completion state so every field must be re-signed against the revised file.
        // Signature rows and audit history are kept — voiding is itself an audited event.
        var voidedFields = document.SignatureFields.Where(f => f.IsSigned).ToList();
        foreach (var field in voidedFields)
            field.VoidSignature();

        foreach (var recipient in document.Recipients)
            recipient.ResetForRevision();

        // The old run's steps describe the superseded round; re-sending rebuilds the workflow
        // from the current recipients through the engine's normal creation path.
        if (document.Workflow is not null)
            db.Workflows.Remove(document.Workflow);

        var oldVersion = document.Version;
        document.ApplyRevision(command.FileName, storagePath, command.FileSizeBytes, pageCount, hash);
        document.MarkIntakePending();

        await db.SaveChangesAsync(cancellationToken);
        queue.EnqueueIntake(document.Id, document.OrganisationId, suggest: false);

        if (voidedFields.Count > 0)
        {
            await auditService.LogAsync(
                document.Id,
                AuditAction.SignaturesVoided,
                "system",
                $"{voidedFields.Count} signed field(s) voided by revision upload — signature records retained as evidence",
                userId,
                actorEmail: currentUser.UserEmail,
                cancellationToken: cancellationToken);
        }

        await auditService.LogAsync(
            document.Id,
            AuditAction.RevisionUploaded,
            "system",
            $"Revision uploaded (v{oldVersion} → v{document.Version})" +
                (string.IsNullOrWhiteSpace(command.ChangeNote) ? "" : $": {command.ChangeNote}"),
            userId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        logger.LogInformation("Revision v{Version} uploaded for document {DocumentId}", document.Version, document.Id);

        return new UploadRevisionResult(document.Id, document.Version, pageCount);
    }
}
