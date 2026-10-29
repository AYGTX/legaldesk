using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.PdfTemplates.Commands;

/// <summary>
/// One configured field on a PDF template — placement (page + coordinates) plus fill responsibility,
/// auto-fill source, dropdown options, and (for Approver fields) pre-assigned approver / workflow stage.
/// </summary>
public record PdfTemplateFieldInput(
    string FieldKey,
    string Label,
    PdfFieldType Type,
    PdfFieldFillRole FillRole,
    int Page,
    float X,
    float Y,
    float Width,
    float Height,
    bool IsRequired,
    int Order,
    string? AutoFillFrom = null,
    string? DefaultValue = null,
    string? OptionsJson = null,
    Guid? PreAssignedApproverId = null,
    int WorkflowStage = 1);

/// <summary>
/// Uploads and persists the base PDF for a new template (admin/manager). The PDF is stored once and
/// reused for every request created from the template; fields are configured afterwards via
/// <see cref="UpdatePdfTemplateCommand"/> (the coordinate designer).
/// </summary>
public record CreatePdfTemplateCommand(
    Stream FileStream,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    string Name,
    /// <summary>
    /// Request type this template carries. Requests created from it inherit this type (and its SLA) at
    /// creation time. Required — every PDF template must be linked to a content type.
    /// </summary>
    Guid RequestTypeId,
    string? Description = null) : ICommand<CreatePdfTemplateResult>;

public record CreatePdfTemplateResult(Guid Id, string Name, int PageCount);

public class CreatePdfTemplateCommandValidator : AbstractValidator<CreatePdfTemplateCommand>
{
    private const long MaxFileSizeBytes = 100 * 1024 * 1024; // 100 MB

    public CreatePdfTemplateCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255)
            .Must(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only PDF files are supported.");
        RuleFor(x => x.FileSizeBytes).GreaterThan(0).LessThanOrEqualTo(MaxFileSizeBytes)
            .WithMessage("File size must be between 1 byte and 100 MB.");
        RuleFor(x => x.ContentType).Must(ct => ct == "application/pdf").WithMessage("Content type must be application/pdf.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.RequestTypeId).NotEmpty().WithMessage("A content type is required.");
    }
}

public class CreatePdfTemplateCommandHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    IVirusScanService virusScan,
    ICurrentUserService currentUser,
    ILogger<CreatePdfTemplateCommandHandler> logger) : ICommandHandler<CreatePdfTemplateCommand, CreatePdfTemplateResult>
{
    public async Task<CreatePdfTemplateResult> HandleAsync(CreatePdfTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageTemplates))
            throw new ForbiddenAccessException();

        var createdById = currentUser.UserId ?? throw new ForbiddenAccessException();

        var requestTypeExists = await db.RequestTypes
            .AnyAsync(t => t.Id == command.RequestTypeId && t.IsActive, cancellationToken);
        if (!requestTypeExists)
            throw new Common.Exceptions.ValidationException("The selected request type is invalid or inactive.");

        var scanResult = await virusScan.ScanAsync(command.FileStream, command.FileName, cancellationToken);
        if (!scanResult.IsSafe)
            throw new InvalidOperationException($"File rejected by virus scanner: {scanResult.ThreatName}");

        command.FileStream.Position = 0;
        var pageCount = await pdfService.GetPageCountAsync(command.FileStream, cancellationToken);
        command.FileStream.Position = 0;

        // Persist the base PDF once; every instance copies from this stored file.
        var storagePath = await storage.UploadAsync(
            command.FileStream, command.FileName, command.ContentType, $"pdf-templates/{createdById}", cancellationToken);

        var template = PdfTemplate.Create(
            command.Name,
            storagePath,
            command.FileName,
            command.FileSizeBytes,
            pageCount,
            createdById,
            command.RequestTypeId,
            command.Description);
        template.SetCreatedBy(createdById.ToString());

        db.PdfTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("PDF template {TemplateId} created by user {UserId} ({PageCount} pages)", template.Id, createdById, pageCount);

        return new CreatePdfTemplateResult(template.Id, template.Name, pageCount);
    }
}
