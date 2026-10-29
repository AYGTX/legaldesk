using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.PdfTemplates.Commands;

/// <summary>
/// An administrator or manager edits the text of a template's PDF in the browser. The edited file becomes the
/// template's base PDF; requests already created from it keep their own copy. Field positions are not touched.
/// </summary>
public record EditPdfTemplateCommand(Guid TemplateId, List<PdfTextEdit> Edits) : ICommand<EditPdfTemplateResult>;

public record EditPdfTemplateResult(Guid TemplateId, int PageCount);

public class EditPdfTemplateCommandValidator : AbstractValidator<EditPdfTemplateCommand>
{
    public EditPdfTemplateCommandValidator()
    {
        RuleFor(x => x.TemplateId).NotEmpty();
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
    }
}

public class EditPdfTemplateCommandHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    IPdfTextEditService editService,
    ICurrentUserService currentUser,
    ILogger<EditPdfTemplateCommandHandler> logger) : ICommandHandler<EditPdfTemplateCommand, EditPdfTemplateResult>
{
    public async Task<EditPdfTemplateResult> HandleAsync(EditPdfTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageTemplates))
            throw new ForbiddenAccessException();
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();

        var template = await db.PdfTemplates
            .FirstOrDefaultAsync(t => t.Id == command.TemplateId, cancellationToken)
            ?? throw new NotFoundException(nameof(PdfTemplate), command.TemplateId);

        await using var original = await storage.DownloadAsync(template.StoragePath, cancellationToken);
        await using var edited = await editService.ApplyEditsAsync(original, command.Edits, cancellationToken);

        var size = edited.Length;
        edited.Position = 0;
        var pageCount = await pdfService.GetPageCountAsync(edited, cancellationToken);
        edited.Position = 0;

        var storagePath = await storage.UploadAsync(
            edited, template.FileName, "application/pdf", $"pdf-templates/{userId}", cancellationToken);

        template.ReplaceFile(storagePath, size, pageCount);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("PDF template {TemplateId} text-edited by {UserId} ({Count} lines)",
            template.Id, userId, command.Edits.Count);

        return new EditPdfTemplateResult(template.Id, pageCount);
    }
}
