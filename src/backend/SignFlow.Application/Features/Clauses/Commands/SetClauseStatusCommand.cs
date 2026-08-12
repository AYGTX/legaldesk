using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Clauses.Commands;

/// <summary>Moves a clause through its approval lifecycle: Draft → Approved → Deprecated.</summary>
public record SetClauseStatusCommand(Guid ClauseId, ClauseStatus Status) : ICommand;

public class SetClauseStatusCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<SetClauseStatusCommandHandler> logger) : ICommandHandler<SetClauseStatusCommand>
{
    public async Task HandleAsync(SetClauseStatusCommand command, CancellationToken cancellationToken = default)
    {
        // Approving wording is a legal sign-off, so it stays with administrators.
        if (!currentUser.IsInRole("Administrator"))
            throw new ForbiddenAccessException();

        var clause = await db.Clauses.FirstOrDefaultAsync(c => c.Id == command.ClauseId, cancellationToken)
            ?? throw new NotFoundException(nameof(Clause), command.ClauseId);

        clause.SetStatus(command.Status);
        clause.SetUpdatedBy(currentUser.UserId!.Value.ToString());
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Clause {ClauseId} set to {Status} by {UserId}",
            clause.Id, command.Status, currentUser.UserId);
    }
}
