using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.RegulatoryWatch.Commands;

/// <summary>Moves a regulatory text through its publication lifecycle: Draft → Published → Archived.</summary>
public record SetRegulatoryTextStatusCommand(Guid RegulatoryTextId, RegulatoryTextStatus Status) : ICommand;

public class SetRegulatoryTextStatusCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    INotificationService notifications,
    ILogger<SetRegulatoryTextStatusCommandHandler> logger) : ICommandHandler<SetRegulatoryTextStatusCommand>
{
    public async Task HandleAsync(SetRegulatoryTextStatusCommand command, CancellationToken cancellationToken = default)
    {
        // Publishing a regulatory change is a legal sign-off, so it stays with administrators.
        if (!currentUser.IsInRole("Administrator"))
            throw new ForbiddenAccessException();

        var text = await db.RegulatoryTexts.FirstOrDefaultAsync(r => r.Id == command.RegulatoryTextId, cancellationToken)
            ?? throw new NotFoundException(nameof(RegulatoryText), command.RegulatoryTextId);

        var wasPublished = text.Status == RegulatoryTextStatus.Published;

        text.SetStatus(command.Status);
        text.SetUpdatedBy(currentUser.UserId!.Value.ToString());
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Regulatory text {RegulatoryTextId} set to {Status} by {UserId}",
            text.Id, command.Status, currentUser.UserId);

        // Regulatory alert: when a text is newly published, warn owners of contracts already in
        // force that this legislative change may affect them.
        if (!wasPublished && command.Status == RegulatoryTextStatus.Published)
            await AlertAffectedContractsAsync(text, cancellationToken);
    }

    private async Task AlertAffectedContractsAsync(RegulatoryText text, CancellationToken cancellationToken)
    {
        var affected = await db.Documents
            .Where(d => d.Status == DocumentStatus.Completed
                        && (text.RequestTypeId == null || d.RequestTypeId == text.RequestTypeId))
            .ToListAsync(cancellationToken);

        if (affected.Count == 0) return;

        foreach (var document in affected)
        {
            await notifications.NotifyUserAsync(
                document.OwnerId,
                NotificationType.RegulatoryAlert,
                "New regulatory change may affect a contract in force",
                $"\"{text.Title}\" was just published and may affect \"{document.Title}\".",
                document.Id,
                cancellationToken);
        }

        logger.LogInformation("Regulatory alert '{Title}' notified {Count} contract owner(s)", text.Title, affected.Count);
    }
}
