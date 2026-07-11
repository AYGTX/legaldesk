using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.RequestTypes.Commands;

public record DeleteRequestTypeCommand(Guid Id) : ICommand;

public class DeleteRequestTypeCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<DeleteRequestTypeCommand>
{
    public async Task HandleAsync(DeleteRequestTypeCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator"))
            throw new ForbiddenAccessException();

        var type = await db.RequestTypes.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(RequestType), command.Id);

        // If documents reference this type, deactivate instead of deleting to preserve history.
        var inUse = await db.Documents.IgnoreQueryFilters().AnyAsync(d => d.RequestTypeId == command.Id, cancellationToken);
        if (inUse)
            throw new ConflictException("This request type is in use by existing requests. Deactivate it instead of deleting.");

        db.RequestTypes.Remove(type);
        await db.SaveChangesAsync(cancellationToken);
    }
}
