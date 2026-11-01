using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Services.Background;

/// <summary>
/// The daily lifecycle sweep (M10), once per organisation at 08:00 in its own time zone (R-M10-8):
/// automatic renewals roll forward (R-M10-2), the alert ladder walks towards each notice deadline (R-M10-3), and confirmed
/// obligations are reminded (R-M10-5). Every message is written in its recipient's language.
/// </summary>
public class LifecycleSweepJob(
    IApplicationDbContext db,
    ITenantContext tenant,
    IPlatformQueries platform,
    INotificationService notifications,
    IEmailService email,
    IAuditService audit,
    UserManager<ApplicationUser> userManager,
    ILogger<LifecycleSweepJob> logger)
{
    /// <summary>The obligation reminders: a week before, then on the day.</summary>
    private static readonly int[] ObligationOffsets = [7, 0];

    public async Task SweepAsync(CancellationToken cancellationToken = default)
    {
        foreach (var organisationId in await platform.ActiveOrganisationIdsAsync(cancellationToken))
        {
            using (tenant.BeginScope(organisationId))
                await SweepOrganisationAsync(organisationId, DateTime.UtcNow, force: false, cancellationToken);
        }
    }

    /// <summary>One organisation's sweep. <paramref name="force"/> skips the 08:00 local check (tests, a manual run).</summary>
    public async Task SweepOrganisationAsync(Guid organisationId, DateTime utcNow, bool force, CancellationToken ct)
    {
        var organisation = await db.Organisations.FirstOrDefaultAsync(o => o.Id == organisationId, ct);
        if (organisation is null) return;

        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, Zone(organisation.TimeZoneId));
        if (!force && (local.Hour < 8 || organisation.LastLifecycleSweepOn == local.Date)) return;
        var today = local.Date;

        var offsets = TermLadder.Offsets(organisation.AlertOffsets);
        var people = new People(db, userManager);
        var sent = 0;

        // 1 — terms: roll renewals forward, then the ladder.
        var terms = await db.ContractTerms
            .Where(t => t.ConfirmedAt != null && t.CurrentEndDate != null)
            .Join(db.Documents.Where(d => d.Status == DocumentStatus.Completed && !d.IsDeleted), t => t.DocumentId, d => d.Id,
                (t, d) => new { Term = t, Document = d })
            .ToListAsync(ct);

        foreach (var (term, document) in terms.Select(x => (x.Term, x.Document)))
        {
            var rolled = term.RollForward(today);
            foreach (var previousEnd in rolled)
            {
                db.ContractEvents.Add(ContractEvent.Record(document.Id, ContractEventKind.Rollover, null,
                    $"{previousEnd:yyyy-MM-dd} -> {term.CurrentEndDate:yyyy-MM-dd}"));
                await audit.LogAsync(document.Id, AuditAction.ContractRolledOver, "system",
                    $"Automatic renewal: the term ending {previousEnd:yyyy-MM-dd} rolled over to {term.CurrentEndDate:yyyy-MM-dd}",
                    cancellationToken: ct);
            }

            if (TermLadder.Due(term, today, offsets) is not { } rung) continue;

            var recipients = new HashSet<Guid> { document.OwnerId };
            if (rung.Escalates)
                foreach (var id in await people.TeamAndManagersAsync(document.OwnerId, ct)) recipients.Add(id);

            foreach (var userId in recipients)
            {
                var french = await people.IsFrenchAsync(userId, ct);
                var subject = TermSubject(rung, term, french);
                var body = TermBody(rung, term, document.Title, today, french);
                await notifications.NotifyUserAsync(userId, NotificationType.ContractTermAlert, subject, body, document.Id, ct);
                await MailAsync(people, userId, "LifecycleAlert", subject, body, document.Id, french, ct);
                sent++;
            }

            if (rung.IsEnd) term.MarkEndAlertSent();
            else term.MarkAlertSent(rung.Anchor, rung.Offset);
        }

        // 2 — obligations: only confirmed ones are reminded (R-M10-5); a one-off missed the day after is marked missed.
        var obligations = await db.Obligations
            .Where(o => o.Confirmed && o.Status == ObligationStatus.Upcoming && o.DueDate != null && o.DueDate <= today.AddDays(ObligationOffsets[0]))
            .Join(db.Documents.Where(d => !d.IsDeleted), o => o.DocumentId, d => d.Id, (o, d) => new { Obligation = o, d.Title, d.OwnerId })
            .ToListAsync(ct);

        foreach (var row in obligations)
        {
            var o = row.Obligation;
            var due = o.DueDate!.Value;
            var days = (due - today).Days;
            if (days < 0)
            {
                if (o.Recurrence == ObligationRecurrence.None) o.MarkMissed();
                continue;
            }
            var offset = ObligationOffsets.Where(x => days <= x).Min();
            if (o.LastReminderFor == due && o.LastReminderOffset is { } last && last <= offset) continue;

            var to = o.OwnerId ?? row.OwnerId;
            var french = await people.IsFrenchAsync(to, ct);
            var oSubject = french ? "Obligation contractuelle à venir" : "Contract obligation coming up";
            var oBody = french
                ? $"« {row.Title} » : {o.Title}, échéance le {due:dd/MM/yyyy}" + (days == 0 ? " (aujourd'hui)." : $" (dans {days} jour(s)).")
                : $"\"{row.Title}\": {o.Title}, due {due:yyyy-MM-dd}" + (days == 0 ? " (today)." : $" (in {days} day(s)).");
            await notifications.NotifyUserAsync(to, NotificationType.ObligationDue, oSubject, oBody, o.DocumentId, ct);
            await MailAsync(people, to, "ObligationDue", oSubject, oBody, o.DocumentId, french, ct);
            o.MarkReminded(due, offset);
            sent++;
        }

        organisation.MarkLifecycleSweep(today);
        await db.SaveChangesAsync(ct);
        if (sent > 0) logger.LogInformation("Lifecycle sweep for {Organisation} sent {Count} alert(s)", organisationId, sent);
    }

    /// <summary>The same alert by e-mail, in the recipient's language (R-M10-8).</summary>
    private async Task MailAsync(People people, Guid userId, string template, string subject, string body, Guid documentId, bool french, CancellationToken ct)
    {
        if (await people.EmailOfAsync(userId, ct) is not { Length: > 0 } address) return;
        await email.SendTemplateAsync(address, template, new { Subject = subject, Body = body, DocumentId = documentId, Language = french ? "fr" : "en" },
            subject, ct);
    }

    private static TimeZoneInfo Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    private static string TermSubject(LadderRung rung, ContractTerm term, bool fr)
    {
        var notice = term.NoticeDeadline is not null && !rung.IsEnd;
        return (rung.IsEnd, notice, rung.Offset, fr) switch
        {
            (true, _, _, true) => "Fin de terme du contrat",
            (true, _, _, false) => "Contract term has ended",
            (_, true, 0, true) => "Dernier jour pour donner congé",
            (_, true, 0, false) => "Last day to give notice",
            (_, true, _, true) => "Date limite de préavis en approche",
            (_, true, _, false) => "Notice deadline approaching",
            (_, false, _, true) => "Fin de contrat en approche",
            _ => "Contract end approaching"
        };
    }

    private static string TermBody(LadderRung rung, ContractTerm term, string title, DateTime today, bool fr)
    {
        var end = term.CurrentEndDate!.Value;
        var auto = term.RenewalMode == RenewalMode.Automatic;
        var days = (rung.Anchor - today).Days;
        if (rung.IsEnd)
            return fr ? $"« {title} » est arrivé à la fin de son terme le {end:dd/MM/yyyy}." : $"\"{title}\" reached the end of its term on {end:yyyy-MM-dd}.";
        if (term.NoticeDeadline is { } deadline)
        {
            var when = days <= 0 ? (fr ? "aujourd'hui" : "today") : fr ? $"dans {days} jour(s), le {deadline:dd/MM/yyyy}" : $"in {days} day(s), on {deadline:yyyy-MM-dd}";
            return fr
                ? $"« {title} » : dernier jour pour donner congé {when}. " + (auto ? $"Sans congé, il se renouvelle le {end:dd/MM/yyyy}." : $"Il prend fin le {end:dd/MM/yyyy}.") + " Enregistrez votre décision pour arrêter ces rappels."
                : $"\"{title}\": last day to give notice {when}. " + (auto ? $"Without notice it renews on {end:yyyy-MM-dd}." : $"It ends on {end:yyyy-MM-dd}.") + " Record your decision to stop these reminders.";
        }
        return fr
            ? $"« {title} » prend fin le {end:dd/MM/yyyy}" + (days > 0 ? $", dans {days} jour(s)." : ", aujourd'hui.") + " Enregistrez votre décision pour arrêter ces rappels."
            : $"\"{title}\" ends on {end:yyyy-MM-dd}" + (days > 0 ? $", in {days} day(s)." : ", today.") + " Record your decision to stop these reminders.";
    }

    /// <summary>Languages and teams, read once per sweep.</summary>
    private sealed class People(IApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        private Dictionary<Guid, string>? _languages;
        private Dictionary<Guid, string?>? _emails;

        public async Task<string?> EmailOfAsync(Guid userId, CancellationToken ct)
        {
            _emails ??= await db.Users.AsNoTracking().Where(u => u.IsActive).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
            return _emails.GetValueOrDefault(userId);
        }
        private List<Guid>? _managers;

        public async Task<bool> IsFrenchAsync(Guid userId, CancellationToken ct)
        {
            _languages ??= await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.PreferredLanguage, ct);
            return !_languages.TryGetValue(userId, out var lang) || !lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The owner's teams and the organisation's managers and administrators (R-M10-3).</summary>
        public async Task<IEnumerable<Guid>> TeamAndManagersAsync(Guid ownerId, CancellationToken ct)
        {
            if (_managers is null)
            {
                var holders = (await userManager.GetUsersInRoleAsync("Manager")).Concat(await userManager.GetUsersInRoleAsync("Administrator"));
                _managers = [.. holders.Where(u => u.IsActive).Select(u => u.Id).Distinct()];
            }
            var teamIds = await db.TeamMembers.Where(m => m.UserId == ownerId).Select(m => m.TeamId).ToListAsync(ct);
            var team = await db.TeamMembers.Where(m => teamIds.Contains(m.TeamId)).Select(m => m.UserId).Distinct().ToListAsync(ct);
            return _managers.Concat(team).Distinct();
        }
    }
}
