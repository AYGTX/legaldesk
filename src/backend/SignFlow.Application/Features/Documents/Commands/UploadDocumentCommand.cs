using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Serialization;
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
    private const long MaxFileSizeBytes = 100 * 1024 * 1024; // 100 MB

    public UploadDocumentCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255)
            .Must(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only PDF files are supported.");
        RuleFor(x => x.FileSizeBytes).GreaterThan(0).LessThanOrEqualTo(MaxFileSizeBytes)
            .WithMessage("File size must be between 1 byte and 100 MB.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ContentType).Must(ct => ct == "application/pdf").WithMessage("Content type must be application/pdf.");
    }
}

public class UploadDocumentCommandHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    IVirusScanService virusScan,
    IAiService aiService,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<UploadDocumentCommandHandler> logger) : ICommandHandler<UploadDocumentCommand, DocumentUploadResult>
{
    public async Task<DocumentUploadResult> HandleAsync(UploadDocumentCommand command, CancellationToken cancellationToken = default)
    {
        var scanResult = await virusScan.ScanAsync(command.FileStream, command.FileName, cancellationToken);
        if (!scanResult.IsSafe)
            throw new InvalidOperationException($"File rejected by virus scanner: {scanResult.ThreatName}");

        command.FileStream.Position = 0;

        var pageCount = await pdfService.GetPageCountAsync(command.FileStream, cancellationToken);
        command.FileStream.Position = 0;

        var hash = await pdfService.ComputeHashAsync(command.FileStream, cancellationToken);
        command.FileStream.Position = 0;

        var extractedText = await pdfService.ExtractTextAsync(command.FileStream, cancellationToken);
        command.FileStream.Position = 0;

        // Fast rules-based pass so the risk card is populated the moment the upload lands. The full
        // legal review is on demand (POST /documents/{id}/analyze) because it takes tens of seconds.
        var riskAnalysis = await aiService.AnalyzeContractRiskAsync(extractedText, command.Title, cancellationToken);
        var obligationSuggestion = await aiService.SuggestObligationAsync(extractedText, command.Title, DateTime.UtcNow, cancellationToken);

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
        document.SetAiRiskAnalysis(JsonSerializer.Serialize(riskAnalysis, ContractJson.Options));
        document.SetObligationSuggestion(obligationSuggestion.DueDate, obligationSuggestion.ObligationType, obligationSuggestion.Rationale);

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
