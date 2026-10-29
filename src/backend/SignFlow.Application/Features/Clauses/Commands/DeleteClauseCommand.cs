using SignFlow.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Clauses.Commands;

public record DeleteClauseCommand(Guid ClauseId) : ICommand;

public class DeleteClauseCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<DeleteClauseCommandHandler> logger) : ICommandHandler<DeleteClauseCommand>
{
    public async Task HandleAsync(DeleteClauseCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ApproveClauses))
            throw new ForbiddenAccessException();

        var clause = await db.Clauses.FirstOrDefaultAsync(c => c.Id == command.ClauseId, cancellationToken)
            ?? throw new NotFoundException(nameof(Clause), command.ClauseId);

        // Wording that has already been inserted into contracts is deprecated rather than removed,
        // so the library still explains language a signed contract contains.
        if (clause.UsageCount > 0)
            throw new ConflictException("This clause has already been used in contracts. Deprecate it instead of deleting it.");

        clause.SoftDelete();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Clause {ClauseId} deleted by {UserId}", clause.Id, currentUser.UserId);
    }
}
