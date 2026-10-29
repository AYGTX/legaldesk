using SignFlow.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.HtmlTemplates.Commands;

public record DeleteHtmlTemplateCommand(Guid Id) : ICommand;

public class DeleteHtmlTemplateCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<DeleteHtmlTemplateCommand>
{
    public async Task HandleAsync(DeleteHtmlTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageTemplates))
            throw new ForbiddenAccessException();

        var template = await db.HtmlTemplates
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(HtmlTemplate), command.Id);

        template.SoftDelete();
        await db.SaveChangesAsync(cancellationToken);
    }
}
