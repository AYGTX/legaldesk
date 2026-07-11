using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Services.Notifications;

public class NotificationService(
    IApplicationDbContext db,
    INotificationHub hub) : INotificationService
{
    public async Task NotifyUserAsync(
        Guid userId,
        NotificationType type,
        string subject,
        string body,
        Guid? documentId = null,
        CancellationToken cancellationToken = default)
    {
        var notification = Notification.Create(type, subject, body, userId, documentId: documentId, isEmail: false, isPush: true);
        notification.MarkSent();
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);

        await hub.SendToUserAsync(userId.ToString(), "notification", new
        {
            id = notification.Id,
            type = type.ToString(),
            subject,
            body,
            documentId,
            createdAt = notification.CreatedAt,
            isRead = false
        }, cancellationToken);
    }

    public async Task NotifyByEmailAsync(
        string email,
        NotificationType type,
        string subject,
        string body,
        Guid? documentId = null,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null) return;

        await NotifyUserAsync(user.Id, type, subject, body, documentId, cancellationToken);
    }
}
