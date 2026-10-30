using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Serialization;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

public record UploadDocumentCommand(
    Stream FileStream,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    string Title,
    string? Description = null,
    string? Subject = null,
    string? Message = null,
    int? ExpirationDays = null,
    Guid? RequestTypeId = null) : ICommand<DocumentUploadResult>;

public record DocumentUploadResult(Guid DocumentId, string Title, int PageCount, string StoragePath);

public class UploadDocumentCommandValidator : AbstractValidator<UploadDocumentCommand>
{
    private const long MaxFileSizeBytes = Common.Services.PdfUploadGuard.MaxBytes; // 50 MB, the same everywhere

    public UploadDocumentCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255)
            .Must(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only PDF files are supported.");
        RuleFor(x => x.FileSizeBytes).GreaterThan(0).LessThanOrEqualTo(MaxFileSizeBytes)
            .WithMessage("File size must be between 1 byte and 50 MB.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ContentType).Must(ct => ct == "application/pdf").WithMessage("Content type must be application/pdf.");
    }
}

public class UploadDocumentCommandHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    Common.Services.PdfUploadGuard uploadGuard,
    IReviewQueue queue,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<UploadDocumentCommandHandler> logger) : ICommandHandler<UploadDocumentCommand, DocumentUploadResult>
{
    public async Task<DocumentUploadResult> HandleAsync(UploadDocumentCommand command, CancellationToken cancellationToken = default)
    {
        // Size, %PDF- signature, readable, not encrypted, virus scan (R-M2-1, R-M2-2).
        var pageCount = await uploadGuard.EnsureSafeAsync(command.FileStream, command.FileName, cancellationToken);

        var hash = await pdfService.ComputeHashAsync(command.FileStream, cancellationToken);
        command.FileStream.Position = 0;

        var userId = currentUser.UserId!.Value;
        var folder = $"documents/{userId}";
        var storagePath = await storage.UploadAsync(command.FileStream, command.FileName, command.ContentType, folder, cancellationToken);

        var document = Document.Create(
            command.Title,
            command.FileName,
            storagePath,
            command.FileSizeBytes,
            pageCount,
            userId,
            command.Description,
            command.ExpirationDays,
            command.Subject,
            command.Message);

        document.SetHash(hash);
        document.SetCreatedBy(userId.ToString());
        // The text is read, the dates proposed and the review run in the background (R-M2-3): the upload answers now.
        document.MarkIntakePending();

        // Apply the request type's SLA — snapshot the deadline from the creation timestamp so that
        // later changes to the request type's SLA never affect this in-flight request.
        if (command.RequestTypeId.HasValue)
        {
            var requestType = await db.RequestTypes
                .FirstOrDefaultAsync(t => t.Id == command.RequestTypeId.Value && t.IsActive, cancellationToken)
                ?? throw new Common.Exceptions.ValidationException("The selected request type is invalid or inactive.");
            document.ApplyRequestType(requestType.Id, requestType.SlaHours);
        }

        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        queue.EnqueueIntake(document.Id, document.OrganisationId, suggest: true);

        await auditService.LogAsync(
            document.Id,
            AuditAction.DocumentCreated,
            "system",
            $"Document '{command.Title}' uploaded",
            userId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        logger.LogInformation("Document {DocumentId} uploaded by user {UserId}", document.Id, userId);

        return new DocumentUploadResult(document.Id, document.Title, pageCount, storagePath);
    }
}
