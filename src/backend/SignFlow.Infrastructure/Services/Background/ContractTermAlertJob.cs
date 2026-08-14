using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Services.Background;

/// <summary>
/// Walks executed contracts through their term and raises one alert per milestone: 90, 60, 30 and
/// 7 days before expiry, again the day the notice deadline passes, and once when the contract
/// lapses. Each rung is recorded on the contract so it fires exactly once.
///
/// From the notice deadline onwards the alert also goes to the legal team (Administrators and
/// Managers), not just the contract owner: that is the point where the organisation has lost the
/// ability to act on its own, and an owner who has left or is on leave must not be the only
/// person who hears about it.
/// </summary>
public class ContractTermAlertJob(
    IApplicationDbContext db,
    INotificationService notifications,
    UserManager<ApplicationUser> userManager,
    ILogger<ContractTermAlertJob> logger)
{
    public async Task SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        // Pre-filter in SQL to the widest rung; the exact milestone is decided by the evaluator,
        // which is where the boundary rules live and where they are unit-tested. Contracts already
        // past their end date stay in scope so the final "expired" rung can still fire.
        var horizon = now.Date.AddDays(ContractTermEvaluator.WidestLadderDays);

        var tracked = await db.Documents
            .Where(d => d.Status == DocumentStatus.Completed
                        && d.ContractEndDate != null
                        && d.ContractEndDate <= horizon)
            .ToListAsync(cancellationToken);

        if (tracked.Count == 0) return;

        var escalationRecipients = new Lazy<Task<List<Guid>>>(GetLegalTeamIdsAsync);
        var alerted = 0;

        foreach (var document in tracked)
        {
            var status = ContractTermEvaluator.Evaluate(
                document.ContractStartDate,
                document.ContractEndDate,
                document.NoticePeriodDays,
                now);

            var stage = ContractTermEvaluator.NextAlertStage(status, document.LastTermAlertStage);
            if (stage is null) continue;

            var subject = SubjectFor(stage);
            var body = BodyFor(document, status, stage);

            await notifications.NotifyUserAsync(
                document.OwnerId, NotificationType.ContractTermAlert,
                subject, body, document.Id, cancellationToken);

            if (ContractTermEvaluator.IsEscalationStage(stage))
            {
                foreach (var userId in await escalationRecipients.Value)
                {
                    // The owner has already been told; don't send the same person two copies.
                    if (userId == document.OwnerId) continue;

                    await notifications.NotifyUserAsync(
                        userId, NotificationType.ContractTermAlert,
                        subject, body, document.Id, cancellationToken);
                }
            }

            document.MarkTermAlertSent(stage);
            alerted++;
        }

        if (alerted == 0) return;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Contract term sweep raised {Count} lifecycle alert(s)", alerted);
    }

    /// <summary>
    /// Administrators and Managers, de-duplicated — the two roles overlap on some accounts.
    /// Resolved once per sweep rather than per contract.
    /// </summary>
    private async Task<List<Guid>> GetLegalTeamIdsAsync()
    {
        var admins = await userManager.GetUsersInRoleAsync("Administrator");
        var managers = await userManager.GetUsersInRoleAsync("Manager");

        return admins.Concat(managers)
            .Select(u => u.Id)
            .Distinct()
            .ToList();
    }

    // Notification copy is English inline, matching every other job and the workflow engine.
    private static string SubjectFor(string stage) => stage switch
    {
        ContractTermEvaluator.StageExpired => "Contract has expired",
        ContractTermEvaluator.StageNoticeDeadline => "Notice deadline reached",
        _ => "Contract expiry approaching"
    };

    private static string BodyFor(Document document, ContractTermStatus status, string stage)
    {
        var endDate = document.ContractEndDate?.ToString("MMM d, yyyy");

        return stage switch
        {
            ContractTermEvaluator.StageExpired =>
                $"\"{document.Title}\" reached the end of its term on {endDate}.",

            ContractTermEvaluator.StageNoticeDeadline when document.AutoRenews =>
                $"\"{document.Title}\" passed its notice deadline of "
                + $"{status.NoticeDeadline:MMM d, yyyy} and renews automatically on {endDate}.",

            ContractTermEvaluator.StageNoticeDeadline =>
                $"\"{document.Title}\" passed its notice deadline of "
                + $"{status.NoticeDeadline:MMM d, yyyy} and ends on {endDate}.",

            _ => $"\"{document.Title}\" ends on {endDate} — {status.DaysToEnd} day(s) remaining."
        };
    }
}
