using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>
/// Owner edits the text of a PDF contract in the browser. The edits are applied to the stored file and the
/// result becomes a new version; the outgoing file is kept as a DocumentVersion, never overwritten.
/// A Draft goes straight to the next version. A ChangesRequested document goes through the normal revision
/// path, so signatures are voided and the approval round restarts exactly as for an uploaded revision.
/// </summary>
public record EditDocumentPdfCommand(Guid DocumentId, List<PdfTextEdit> Edits, string? ChangeNote) : ICommand<UploadRevisionResult>;

public class EditDocumentPdfCommandValidator : AbstractValidator<EditDocumentPdfCommand>
{
    public EditDocumentPdfCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.Edits).NotEmpty().WithMessage("Make at least one edit.");
        RuleFor(x => x.Edits).Must(e => e.Count <= 500).WithMessage("Too many edits in one save.");
        RuleForEach(x => x.Edits).ChildRules(e =>
        {
            e.RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
            e.RuleFor(x => x.Text).MaximumLength(1000);
            e.RuleFor(x => x.Width).GreaterThan(0);
            e.RuleFor(x => x.Height).GreaterThan(0);
            e.RuleFor(x => x.FontSize).InclusiveBetween(4, 72);
        });
        RuleFor(x => x.ChangeNote).MaximumLength(2000);
    }
}

public class EditDocumentPdfCommandHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    IPdfTextEditService editService,
    ICommandHandler<UploadRevisionCommand, UploadRevisionResult> revisionHandler,
    IReviewQueue queue,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<EditDocumentPdfCommandHandler> logger) : ICommandHandler<EditDocumentPdfCommand, UploadRevisionResult>
{
    public async Task<UploadRevisionResult> HandleAsync(EditDocumentPdfCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();
        if (document.OwnerId != userId)
            throw new ForbiddenAccessException();

        if (document.HtmlTemplateId.HasValue || document.StoragePath.StartsWith("html-templates/"))
            throw new ConflictException("Only PDF-based contracts can be edited this way.");

        if (document.PdfTemplateId.HasValue)
            throw new ConflictException("This contract was built from a template: its PDF cannot be edited, only its answers revised.");

        if (document.Status is not (DocumentStatus.Draft or DocumentStatus.ChangesRequested))
            throw new ConflictException("A contract can only be edited while it is a draft or after changes were requested.");

        await using var original = await storage.DownloadAsync(document.StoragePath, cancellationToken);
        await using var edited = await editService.ApplyEditsAsync(original, command.Edits, cancellationToken);

        var size = edited.Length;
        var note = string.IsNullOrWhiteSpace(command.ChangeNote)
            ? $"Text edited in the browser ({command.Edits.Count} line(s))"
            : command.ChangeNote;

        if (document.Status == DocumentStatus.ChangesRequested)
        {
            edited.Position = 0;
            return await revisionHandler.HandleAsync(
                new UploadRevisionCommand(document.Id, edited, document.FileName, size, "application/pdf", note),
                cancellationToken);
        }

        edited.Position = 0;
        var pageCount = await pdfService.GetPageCountAsync(edited, cancellationToken);
        edited.Position = 0;
        var hash = await pdfService.ComputeHashAsync(edited, cancellationToken);
        edited.Position = 0;

        var previous = DocumentVersion.Create(
            document.Id, document.Version, document.StoragePath, document.FileSizeBytes, userId, note);
        previous.SetCreatedBy(userId.ToString());
        db.DocumentVersions.Add(previous);

        var storagePath = await storage.UploadAsync(
            edited, document.FileName, "application/pdf", $"documents/{userId}", cancellationToken);

        var oldVersion = document.Version;
        document.ApplyRevision(document.FileName, storagePath, size, pageCount, hash);
        document.MarkIntakePending();
        await db.SaveChangesAsync(cancellationToken);
        queue.EnqueueIntake(document.Id, document.OrganisationId, suggest: false);

        await auditService.LogAsync(
            document.Id,
            AuditAction.RevisionUploaded,
            "system",
            $"Text edited in the browser (v{oldVersion} → v{document.Version}): {command.Edits.Count} line(s) changed",
            userId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        logger.LogInformation("Document {DocumentId} text-edited to v{Version} ({Count} lines)",
            document.Id, document.Version, command.Edits.Count);

        return new UploadRevisionResult(document.Id, document.Version, pageCount);
    }
}
