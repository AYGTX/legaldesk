using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Clauses.Commands;

/// <summary>
/// Records that a drafter inserted this clause into a contract template. Drives the "most used"
/// ordering in the clause picker and shows the legal team which wording actually circulates.
/// </summary>
public record RecordClauseUsageCommand(Guid ClauseId) : ICommand;

public class RecordClauseUsageCommandHandler(
    IApplicationDbContext db) : ICommandHandler<RecordClauseUsageCommand>
{
    public async Task HandleAsync(RecordClauseUsageCommand command, CancellationToken cancellationToken = default)
    {
        var clause = await db.Clauses.FirstOrDefaultAsync(c => c.Id == command.ClauseId, cancellationToken)
            ?? throw new NotFoundException(nameof(Clause), command.ClauseId);

        clause.RecordUsage();
        await db.SaveChangesAsync(cancellationToken);
    }
}
