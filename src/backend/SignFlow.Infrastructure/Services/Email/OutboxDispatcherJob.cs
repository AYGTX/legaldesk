using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Services.Email;

/// <summary>
/// Sends what the outbox holds (R-M4-9): every minute, per organisation, the due messages in order. A failure is
/// recorded on the message and retried later (1, 5, 15, 60, 240 minutes); after the fifth it is given up and
/// stays in the table with its last error, for an administrator to see.
/// </summary>
public class OutboxDispatcherJob(
    IApplicationDbContext db,
    ITenantContext tenant,
    IPlatformQueries platform,
    ISmtpSender smtp,
    ILogger<OutboxDispatcherJob> logger)
{
    public const int BatchSize = 50;

    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task DispatchAsync(CancellationToken cancellationToken = default)
    {
        foreach (var organisationId in await platform.ActiveOrganisationIdsAsync(cancellationToken))
        {
            using (tenant.BeginScope(organisationId))
                await DispatchOrganisationAsync(cancellationToken);
        }
    }

    private async Task DispatchOrganisationAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var due = await db.OutboxMessages
            .Where(m => m.SentAt == null && m.NextAttemptAt != null && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in due)
        {
            try
            {
                if (message.Kind != OutboxMessage.EmailKind)
                    throw new InvalidOperationException($"Unknown outbox message kind '{message.Kind}'.");

                var email = JsonSerializer.Deserialize<EmailMessage>(message.PayloadJson, OutboxEmailService.Json)
                            ?? throw new InvalidOperationException("Empty e-mail payload.");
                await smtp.SendAsync(email, cancellationToken);
                message.MarkSent(DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkFailed($"{ex.GetType().Name}: {ex.Message}", DateTime.UtcNow);
                if (message.GaveUp)
                    logger.LogError(ex, "Outbox message {MessageId} given up after {Attempts} attempts", message.Id, message.Attempts);
                else
                    logger.LogWarning(ex, "Outbox message {MessageId} failed (attempt {Attempts}); next try at {Next}",
                        message.Id, message.Attempts, message.NextAttemptAt);
            }

            // Save after each message: one that was sent must never be sent again because a later one failed.
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
