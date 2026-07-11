using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Notifications.Queries;

public record GetMyNotificationsQuery(int Take = 30) : IQuery<NotificationsResultDto>;

public record NotificationsResultDto(int UnreadCount, List<NotificationDto> Items);

public record NotificationDto(
    Guid Id,
    NotificationType Type,
    string Subject,
    string Body,
    bool IsRead,
    Guid? DocumentId,
    DateTime CreatedAt);

public class GetMyNotificationsQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetMyNotificationsQuery, NotificationsResultDto>
{
    public async Task<NotificationsResultDto> HandleAsync(GetMyNotificationsQuery query, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();

        var mine = db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.IsPush);

        var unreadCount = await mine.CountAsync(n => !n.IsRead, cancellationToken);

        var items = await mine
            .OrderByDescending(n => n.CreatedAt)
            .Take(query.Take)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Subject, n.Body, n.IsRead, n.DocumentId, n.CreatedAt))
            .ToListAsync(cancellationToken);

        return new NotificationsResultDto(unreadCount, items);
    }
}
