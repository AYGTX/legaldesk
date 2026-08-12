using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.RegulatoryWatch.Commands;

public record DeleteRegulatoryTextCommand(Guid RegulatoryTextId) : ICommand;

public class DeleteRegulatoryTextCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<DeleteRegulatoryTextCommandHandler> logger) : ICommandHandler<DeleteRegulatoryTextCommand>
{
    public async Task HandleAsync(DeleteRegulatoryTextCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator"))
            throw new ForbiddenAccessException();

        var text = await db.RegulatoryTexts.FirstOrDefaultAsync(r => r.Id == command.RegulatoryTextId, cancellationToken)
            ?? throw new NotFoundException(nameof(RegulatoryText), command.RegulatoryTextId);

        // A published text may already have alerted contracts in force; archive it instead of
        // deleting it so that history stays explainable.
        if (text.Status != RegulatoryTextStatus.Draft)
            throw new ConflictException("This regulatory text has been published. Archive it instead of deleting it.");

        text.SoftDelete();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Regulatory text {RegulatoryTextId} deleted by {UserId}", text.Id, currentUser.UserId);
    }
}
