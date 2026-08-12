using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Services.Background;

/// <summary>
/// Periodically reminds document owners of upcoming contract obligations (renewal/expiry dates)
/// on executed contracts, at most once every 7 days once the due date is within 30 days.
/// </summary>
public class ObligationReminderJob(
    IApplicationDbContext db,
    INotificationService notifications,
    ILogger<ObligationReminderJob> logger)
{
    public async Task SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var due = await db.Documents
            .Where(d => d.Status == DocumentStatus.Completed
                        && d.ObligationDueDate != null
                        && d.ObligationDueDate <= now.AddDays(30)
                        && (d.LastObligationReminderSentAt == null || d.LastObligationReminderSentAt < now.AddDays(-7)))
            .ToListAsync(cancellationToken);

        if (due.Count == 0) return;

        foreach (var document in due)
        {
            await notifications.NotifyUserAsync(
                document.OwnerId,
                NotificationType.ObligationDue,
                "Contract obligation approaching",
                $"\"{document.Title}\" has a {document.ObligationType?.ToLowerInvariant()} obligation due {document.ObligationDueDate:MMM d, yyyy}.",
                document.Id,
                cancellationToken);

            document.MarkObligationReminderSent();
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Obligation sweep reminded on {Count} document(s)", due.Count);
    }
}
