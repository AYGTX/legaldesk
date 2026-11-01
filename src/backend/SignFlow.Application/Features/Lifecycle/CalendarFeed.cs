using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Lifecycle;

/// <summary>
/// A calendar feed per user (R-M10-9): the notice deadlines and end dates of their contracts and the obligations they
/// own, as all-day events in an .ics file any calendar can subscribe to. The address carries a secret token; only its
/// hash is stored, and issuing a new one or revoking it stops the old address at once.
/// </summary>
public record IssueCalendarTokenCommand : ICommand<string>;

public record RevokeCalendarTokenCommand : ICommand;

/// <summary>The feed itself, read through the token (no sign-in: calendars cannot sign in).</summary>
public record GetCalendarFeedQuery(string Token) : IQuery<string>;

public class IssueCalendarTokenCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : ICommandHandler<IssueCalendarTokenCommand, string>
{
    public async Task<string> HandleAsync(IssueCalendarTokenCommand command, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstAsync(u => u.Id == currentUser.UserId, cancellationToken);
        var token = SecureTokens.Generate();
        user.CalendarTokenHash = SecureTokens.Hash(token);
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }
}

public class RevokeCalendarTokenCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : ICommandHandler<RevokeCalendarTokenCommand>
{
    public async Task HandleAsync(RevokeCalendarTokenCommand command, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstAsync(u => u.Id == currentUser.UserId, cancellationToken);
        user.CalendarTokenHash = null;
        await db.SaveChangesAsync(cancellationToken);
    }
}

public class GetCalendarFeedQueryHandler(IApplicationDbContext db, ITenantContext tenant, IPlatformQueries platform) : IQueryHandler<GetCalendarFeedQuery, string>
{
    public async Task<string> HandleAsync(GetCalendarFeedQuery query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.Token) || query.Token.Length > 200) throw new NotFoundException("Calendar feed not found.");
        var hash = SecureTokens.Hash(query.Token);
        var owner = await platform.UserOfCalendarTokenAsync(hash, cancellationToken)
            ?? throw new NotFoundException("Calendar feed not found.");

        using (tenant.BeginScope(owner.OrganisationId))
        {
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == owner.UserId, cancellationToken);
            var french = !user.PreferredLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase);
            var terms = await db.ContractTerms.AsNoTracking()
                .Where(t => t.ConfirmedAt != null && t.CurrentEndDate != null && t.Decision != TermDecision.Terminate && t.Decision != TermDecision.LetExpire)
                .Join(db.Documents.Where(d => d.OwnerId == user.Id && !d.IsDeleted && d.Status == DocumentStatus.Completed),
                    t => t.DocumentId, d => d.Id, (t, d) => new { Term = t, d.Id, d.Title })
                .ToListAsync(cancellationToken);
            var obligations = await db.Obligations.AsNoTracking()
                .Where(o => o.Confirmed && o.Status == ObligationStatus.Upcoming && o.DueDate != null && o.OwnerId == user.Id)
                .Join(db.Documents.Where(d => !d.IsDeleted), o => o.DocumentId, d => d.Id, (o, d) => new { Obligation = o, d.Title })
                .ToListAsync(cancellationToken);

            var ics = new StringBuilder();
            Line(ics, "BEGIN:VCALENDAR");
            Line(ics, "VERSION:2.0");
            Line(ics, "PRODID:-//LegalDesk//Contract dates//EN");
            Line(ics, "CALSCALE:GREGORIAN");
            Line(ics, $"X-WR-CALNAME:{Escape(french ? "LegalDesk — échéances" : "LegalDesk — contract dates")}");
            foreach (var row in terms)
            {
                if (row.Term.NoticeDeadline is { } deadline)
                    Event(ics, $"notice-{row.Id}-{deadline:yyyyMMdd}", deadline,
                        french ? $"Dernier jour de préavis : {row.Title}" : $"Last day to give notice: {row.Title}");
                Event(ics, $"end-{row.Id}-{row.Term.CurrentEndDate:yyyyMMdd}", row.Term.CurrentEndDate!.Value,
                    (row.Term.RenewalMode == RenewalMode.Automatic
                        ? (french ? "Renouvellement automatique : " : "Automatic renewal: ")
                        : (french ? "Fin de contrat : " : "Contract ends: ")) + row.Title);
            }
            foreach (var row in obligations)
                Event(ics, $"obligation-{row.Obligation.Id}-{row.Obligation.DueDate:yyyyMMdd}", row.Obligation.DueDate!.Value,
                    $"{row.Obligation.Title} — {row.Title}");
            Line(ics, "END:VCALENDAR");
            return ics.ToString();
        }
    }

    private static void Event(StringBuilder ics, string uid, DateTime date, string summary)
    {
        Line(ics, "BEGIN:VEVENT");
        Line(ics, $"UID:{uid}@legaldesk");
        Line(ics, $"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}");
        Line(ics, $"DTSTART;VALUE=DATE:{date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}");
        Line(ics, $"DTEND;VALUE=DATE:{date.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture)}");
        Line(ics, $"SUMMARY:{Escape(summary)}");
        Line(ics, "END:VEVENT");
    }

    /// <summary>RFC 5545: CRLF line ends, and backslash, comma, semicolon and newline escaped in text.</summary>
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");

    private static void Line(StringBuilder ics, string line) => ics.Append(line).Append("\r\n");
}
