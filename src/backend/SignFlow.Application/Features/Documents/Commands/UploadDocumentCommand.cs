using System.Text.Json;
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
    IAiService aiService,
    ILegalPlaybookService playbook,
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

        var extractedText = await pdfService.ExtractTextAsync(command.FileStream, cancellationToken);
        command.FileStream.Position = 0;

        // Fast rules-based pass so the risk card is populated the moment the upload lands. The full
        // legal review is on demand (POST /documents/{id}/analyze) because it takes tens of seconds; attaching
        // the playbook's verified law citations here is cheap (cached playbook, local matching) and degrades
        // to plain findings if the legal-corpus service is unreachable, so there is no reason to make the
        // reader wait for "Analyze" just to see which article backs a flagged risk.
        var riskAnalysis = await aiService.AnalyzeContractRiskAsync(extractedText, command.Title, cancellationToken);
        riskAnalysis = await playbook.AttachToRiskAsync(riskAnalysis, extractedText, cancellationToken);
        var obligationSuggestion = await aiService.SuggestObligationAsync(extractedText, command.Title, DateTime.UtcNow, cancellationToken);
        var proposedTerm = await ProposeTermAsync(extractedText, command.Title, cancellationToken);

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
        if (proposedTerm?.EndDate is not null)
            document.SuggestContractTerm(proposedTerm.StartDate, proposedTerm.EndDate, proposedTerm.AutoRenews ?? false, proposedTerm.NoticePeriodDays);

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

    /// <summary>The contract's term as the AI assistant reads it (dates backed by a verified excerpt), so the contract
    /// appears in the schedule without anyone typing its dates; null when no model is configured or nothing is found.
    /// A failure here never blocks the upload.</summary>
    private async Task<SuggestedTermDto?> ProposeTermAsync(string text, string title, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var terms = await ExtractKeyTermsCommandHandler.ReadAsync(aiService, text, title, "fr", ct);
            return ExtractKeyTermsCommandHandler.Suggest(terms);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation(ex, "No contract term proposed at upload");
            return null;
        }
    }
}
