using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Lifecycle;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Services.Background;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// The daily lifecycle sweep (M10): at 08:00 in the organisation's time zone, alerts in each recipient's language,
/// obligations reminded only when confirmed, and a calendar feed of the same dates.
/// </summary>
public class LifecycleSweepTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private static async Task<(ScenarioFixture F, Document Document)> SeedAsync(string ownerLanguage)
    {
        var f = new ScenarioFixture(OwnerId);
        f.Db.Organisations.Add(Organisation.Create("Org", "org", f.OrganisationId));
        var users = f.GetHandler<UserManager<ApplicationUser>>();
        Assert.True((await users.CreateAsync(new ApplicationUser
        {
            Id = OwnerId, FirstName = "Owner", LastName = "Test", UserName = "owner@test.com", Email = "owner@test.com",
            IsActive = true, PreferredLanguage = ownerLanguage
        })).Succeeded);
        var document = Document.Create("Contrat de maintenance", "c.pdf", "documents/c.pdf", 100, 1, OwnerId);
        document.SeedStatus(DocumentStatus.Completed);
        f.Db.Documents.Add(document);
        await f.Db.SaveChangesAsync();
        return (f, document);
    }

    private static LifecycleSweepJob Job(ScenarioFixture f) => new(
        f.Db, f.GetHandler<ITenantContext>(), f.GetHandler<IPlatformQueries>(), f.GetHandler<INotificationService>(),
        f.GetHandler<IEmailService>(), f.GetHandler<IAuditService>(), f.GetHandler<UserManager<ApplicationUser>>(),
        NullLogger<LifecycleSweepJob>.Instance);

    private static Task SetTermAsync(ScenarioFixture f, Guid documentId, DateTime end, int noticeDays, RenewalMode renewal = RenewalMode.None) =>
        new SetContractTermCommandHandler(f.Db, f.GetHandler<ICurrentUserService>()).HandleAsync(
            new SetContractTermCommand(documentId, end.AddYears(-1), end, renewal, renewal == RenewalMode.Automatic ? 12 : null,
                noticeDays, NoticeUnit.Days, NoticeForm.RegisteredMail));

    private static NullEmailService Mail(ScenarioFixture f) => (NullEmailService)f.GetHandler<IEmailService>();

    [Theory]
    [InlineData("fr", "Date limite de préavis en approche")]
    [InlineData("en", "Notice deadline approaching")]
    public async Task The_alert_is_written_in_its_recipients_language(string language, string subject)
    {
        var (f, document) = await SeedAsync(language);
        using var _ = f;
        await SetTermAsync(f, document.Id, new DateTime(2026, 12, 31), 90);

        // 3 August 2026, 09:00 in Tunis: 60 days before the notice deadline of 2 October.
        await Job(f).SweepOrganisationAsync(f.OrganisationId, new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc), force: false, default);

        var alert = Assert.Single(f.Db.Notifications.Where(n => n.Type == NotificationType.ContractTermAlert));
        Assert.Equal(subject, alert.Subject);
        Assert.Single(Mail(f).Sent, m => m.Template == "LifecycleAlert" && m.To == "owner@test.com");
    }

    [Fact]
    public async Task Nothing_is_sent_before_eight_in_the_morning_local_time_and_once_a_day()
    {
        var (f, document) = await SeedAsync("fr");
        using var _ = f;
        await SetTermAsync(f, document.Id, new DateTime(2026, 12, 31), 90);

        await Job(f).SweepOrganisationAsync(f.OrganisationId, new DateTime(2026, 8, 3, 6, 30, 0, DateTimeKind.Utc), false, default); // 07:30 in Tunis
        Assert.Empty(f.Db.Notifications);

        await Job(f).SweepOrganisationAsync(f.OrganisationId, new DateTime(2026, 8, 3, 7, 5, 0, DateTimeKind.Utc), false, default); // 08:05
        await Job(f).SweepOrganisationAsync(f.OrganisationId, new DateTime(2026, 8, 3, 15, 0, 0, DateTimeKind.Utc), false, default);
        Assert.Single(f.Db.Notifications);
    }

    [Fact]
    public async Task An_automatic_renewal_rolls_over_on_its_end_date_and_records_it()
    {
        var (f, document) = await SeedAsync("fr");
        using var _ = f;
        await SetTermAsync(f, document.Id, new DateTime(2026, 12, 31), 30, RenewalMode.Automatic);

        await Job(f).SweepOrganisationAsync(f.OrganisationId, new DateTime(2027, 1, 1, 9, 0, 0, DateTimeKind.Utc), true, default);

        var term = await f.Db.ContractTerms.SingleAsync();
        Assert.Equal(new DateTime(2027, 12, 31), term.CurrentEndDate);
        Assert.Single(f.Db.ContractEvents.Where(e => e.Kind == ContractEventKind.Rollover));
    }

    [Fact]
    public async Task Only_a_confirmed_obligation_is_reminded_and_a_suggestion_needs_its_quote()
    {
        var (f, document) = await SeedAsync("en");
        using var _ = f;
        var add = new AddObligationCommandHandler(f.Db, f.GetHandler<ICurrentUserService>());
        await add.HandleAsync(new AddObligationCommand(document.Id, "Insurance certificate", ObligationKind.InsuranceCertificate,
            new DateTime(2026, 9, 7), ObligationRecurrence.Yearly, null, null, ObligationSource.Person));
        await add.HandleAsync(new AddObligationCommand(document.Id, "Quarterly report", ObligationKind.Report,
            new DateTime(2026, 9, 7), ObligationRecurrence.None, null, "Le prestataire remet un rapport chaque trimestre.", ObligationSource.Harvey));
        await Assert.ThrowsAsync<ValidationException>(() => add.HandleAsync(new AddObligationCommand(document.Id, "Guessed", ObligationKind.Other,
            new DateTime(2026, 9, 7), ObligationRecurrence.None, null, null, ObligationSource.Harvey)));

        await Job(f).SweepOrganisationAsync(f.OrganisationId, new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc), true, default);

        var reminder = Assert.Single(f.Db.Notifications.Where(n => n.Type == NotificationType.ObligationDue));
        Assert.Contains("Insurance certificate", reminder.Body);
    }

    [Fact]
    public async Task The_calendar_feed_lists_the_dates_and_stops_when_revoked()
    {
        var (f, document) = await SeedAsync("fr");
        using var _ = f;
        await SetTermAsync(f, document.Id, new DateTime(2026, 12, 31), 90);
        var token = await new IssueCalendarTokenCommandHandler(f.Db, f.GetHandler<ICurrentUserService>()).HandleAsync(new IssueCalendarTokenCommand());

        var ics = await new GetCalendarFeedQueryHandler(f.Db, f.GetHandler<ITenantContext>(), f.GetHandler<IPlatformQueries>()).HandleAsync(new GetCalendarFeedQuery(token));

        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20261002", ics);
        Assert.Contains("SUMMARY:Dernier jour de préavis : Contrat de maintenance", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20261231", ics);

        await new RevokeCalendarTokenCommandHandler(f.Db, f.GetHandler<ICurrentUserService>()).HandleAsync(new RevokeCalendarTokenCommand());
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetCalendarFeedQueryHandler(f.Db, f.GetHandler<ITenantContext>(), f.GetHandler<IPlatformQueries>()).HandleAsync(new GetCalendarFeedQuery(token)));
    }
}
