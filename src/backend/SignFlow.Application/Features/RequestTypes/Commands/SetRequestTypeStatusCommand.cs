using SignFlow.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.RequestTypes.Commands;

public record SetRequestTypeStatusCommand(Guid Id, bool IsActive) : ICommand;

public class SetRequestTypeStatusCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<SetRequestTypeStatusCommand>
{
    public async Task HandleAsync(SetRequestTypeStatusCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageRequestTypes))
            throw new ForbiddenAccessException();

        var type = await db.RequestTypes.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(RequestType), command.Id);

        type.SetActive(command.IsActive);
        await db.SaveChangesAsync(cancellationToken);
    }
}
