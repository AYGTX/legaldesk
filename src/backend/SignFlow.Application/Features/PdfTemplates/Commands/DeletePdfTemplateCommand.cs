using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.PdfTemplates.Commands;

public record DeletePdfTemplateCommand(Guid Id) : ICommand;

public class DeletePdfTemplateCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<DeletePdfTemplateCommand>
{
    public async Task HandleAsync(DeletePdfTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator") && !currentUser.IsInRole("Manager"))
            throw new ForbiddenAccessException();

        var template = await db.PdfTemplates
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(PdfTemplate), command.Id);

        // Soft-delete the config only; the stored base PDF and any in-flight documents are untouched.
        template.SoftDelete();
        await db.SaveChangesAsync(cancellationToken);
    }
}
