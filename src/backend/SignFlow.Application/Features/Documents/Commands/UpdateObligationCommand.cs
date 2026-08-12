using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>Owner (or administrator) confirmation/override of the AI-suggested obligation date.</summary>
public record UpdateObligationCommand(Guid DocumentId, DateTime DueDate, string ObligationType) : ICommand;

public class UpdateObligationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<UpdateObligationCommandHandler> logger) : ICommandHandler<UpdateObligationCommand>
{
    public async Task HandleAsync(UpdateObligationCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        var isAdmin = currentUser.IsInRole("Administrator");
        if (!isAdmin && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        document.ConfirmObligation(command.DueDate, command.ObligationType);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Document {DocumentId} obligation confirmed for {DueDate} by user {UserId}",
            command.DocumentId, command.DueDate, currentUser.UserId);
    }
}
