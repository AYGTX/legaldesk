using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.Notifications.Commands;

/// <summary>Marks a single notification (when Id is set) or all of the user's notifications as read.</summary>
public record MarkNotificationsReadCommand(Guid? NotificationId = null) : ICommand;

public class MarkNotificationsReadCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<MarkNotificationsReadCommand>
{
    public async Task HandleAsync(MarkNotificationsReadCommand command, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();

        var query = db.Notifications.Where(n => n.UserId == userId && !n.IsRead);
        if (command.NotificationId.HasValue)
            query = query.Where(n => n.Id == command.NotificationId.Value);

        var toMark = await query.ToListAsync(cancellationToken);
        foreach (var n in toMark)
            n.MarkRead();

        if (toMark.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }
}
