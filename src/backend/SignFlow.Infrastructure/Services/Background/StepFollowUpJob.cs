using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Infrastructure.Services.Background;

/// <summary>
/// Every hour, reminds the participants whose open step is due a reminder and escalates the steps waiting longer than
/// their escalation delay (R-M4-6), one organisation at a time. E-mails go through the outbox, so a mail server that is
/// down delays them without losing them (R-M4-9).
/// </summary>
public class StepFollowUpJob(
    IWorkflowEngine engine,
    ITenantContext tenant,
    IPlatformQueries platform,
    ILogger<StepFollowUpJob> logger)
{
    public async Task SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var organisationId in await platform.ActiveOrganisationIdsAsync(cancellationToken))
        {
            using (tenant.BeginScope(organisationId))
            {
                var sent = await engine.FollowUpAsync(now, cancellationToken);
                if (sent > 0)
                    logger.LogInformation("Follow-up sent {Count} reminder(s) or escalation(s) for organisation {OrganisationId}", sent, organisationId);
            }
        }
    }
}
