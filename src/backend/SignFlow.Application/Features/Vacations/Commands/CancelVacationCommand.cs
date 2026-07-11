using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Vacations.Commands;

public record CancelVacationCommand(Guid VacationId) : ICommand;

public class CancelVacationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<CancelVacationCommand>
{
    public async Task HandleAsync(CancelVacationCommand command, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();

        var vacation = await db.Vacations
            .FirstOrDefaultAsync(v => v.Id == command.VacationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Vacation), command.VacationId);

        if (vacation.UserId != userId)
            throw new ForbiddenAccessException();

        db.Vacations.Remove(vacation);
        await db.SaveChangesAsync(cancellationToken);
    }
}
