using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Services.Background;

/// <summary>
/// Periodically marks in-progress requests whose SLA deadline has passed as Expired,
/// preventing further signatures until an administrator reopens them.
/// </summary>
public class SlaExpirationJob(
    IApplicationDbContext db,
    INotificationService notifications,
    ILogger<SlaExpirationJob> logger)
{
    public async Task SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var overdue = await db.Documents
            .Where(d => d.Status == DocumentStatus.InProgress
                        && d.ExpiresAt != null
                        && d.ExpiresAt < now)
            .ToListAsync(cancellationToken);

        if (overdue.Count == 0) return;

        foreach (var document in overdue)
            document.SetStatus(DocumentStatus.Expired);

        await db.SaveChangesAsync(cancellationToken);

        foreach (var document in overdue)
        {
            await notifications.NotifyUserAsync(
                document.OwnerId,
                NotificationType.Expired,
                "Request expired",
                $"\"{document.Title}\" passed its signing deadline and was marked as Expired.",
                document.Id,
                cancellationToken);
        }

        logger.LogInformation("SLA sweep marked {Count} request(s) as Expired", overdue.Count);
    }
}
