using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.Recipients.Commands;
using SignFlow.Application.Features.Signing;
using SignFlow.Application.Features.Signing.Commands;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Services.Background;
using SignFlow.Infrastructure.Services.Email;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// The circuit (M4, R-M7-2): rounds, stages under the All and Any rules, team steps shared by their members, hand-over by
/// the participant or to the stand-in of someone away, reminders and escalation, and a revision opening the next round.
/// </summary>
public class CircuitTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private const string OwnerEmail = "owner@test.com";

    private static async Task<ScenarioFixture> FixtureAsync()
    {
        var f = new ScenarioFixture(OwnerId, OwnerEmail);
        await AddUserAsync(f, "Owner", OwnerEmail, OwnerId);
        return f;
    }

    private static async Task<ApplicationUser> AddUserAsync(ScenarioFixture f, string first, string email, Guid? id = null)
    {
        var user = new ApplicationUser
        {
            Id = id ?? Guid.NewGuid(), FirstName = first, LastName = "Test", UserName = email, Email = email,
            IsActive = true, EmailConfirmed = true
        };
        Assert.True((await f.GetHandler<UserManager<ApplicationUser>>().CreateAsync(user)).Succeeded);
        return user;
    }

    /// <summary>A draft contract with the given participants: (name, e-mail, order, rule).</summary>
    private static async Task<Document> ContractAsync(ScenarioFixture f, WorkflowType type, params (string Name, string Email, int Order, StageRule Rule)[] people)
    {
        var document = Document.Create("Contrat de services", "contrat.pdf", "documents/contrat.pdf", 1000, 1, OwnerId);
        document.SetWorkflowType(type);
        f.Db.Documents.Add(document);
        foreach (var p in people)
        {
            var recipient = Recipient.Create(document.Id, p.Name, p.Email, p.Order, ApprovalType.Approval);
            recipient.SetCircuit(p.Rule, null, null, null, 3, null, null);
            f.Db.Recipients.Add(recipient);
        }
        await f.Db.SaveChangesAsync();
        return document;
    }

    private static Task<Recipient> RecipientAsync(ScenarioFixture f, string email) =>
        f.Db.Recipients.FirstAsync(r => r.Email == email);

    private static NullEmailService Mail(ScenarioFixture f) => (NullEmailService)f.GetHandler<IEmailService>();

    private static string LinkOf(ScenarioFixture f, string email) =>
        Mail(f).Last(email, "DocumentReceived", "SigningLink").Replace("/sign/", "");

    [Fact]
    public async Task Any_stage_closes_on_the_first_answer_and_the_others_can_no_longer_act()
    {
        using var f = await FixtureAsync();
        var document = await ContractAsync(f, WorkflowType.Mixed,
            ("Ali", "ali@client.tn", 1, StageRule.Any), ("Beya", "beya@client.tn", 1, StageRule.Any), ("Chams", "chams@client.tn", 2, StageRule.All));
        var engine = f.GetHandler<IWorkflowEngine>();
        await engine.StartAsync(document.Id);
        var beyaLink = LinkOf(f, "beya@client.tn");

        var ali = await RecipientAsync(f, "ali@client.tn");
        await engine.AdvanceAsync(document.Id, ali.Id);

        var beya = await RecipientAsync(f, "beya@client.tn");
        Assert.Equal(WorkflowStepStatus.Skipped, beya.Status);
        Assert.True(await engine.IsTurnAsync((await RecipientAsync(f, "chams@client.tn")).Id));
        var refused = await Assert.ThrowsAsync<SigningRefusedException>(() =>
            f.GetHandler<ISigningAccess>().ResolveAsync(beyaLink, null, SigningNeed.Link));
        Assert.Equal("link.expired", refused.Code);
        Assert.Contains(f.Db.WorkflowSteps, s => s.RecipientId == beya.Id && s.Status == WorkflowStepStatus.Skipped);
    }

    [Fact]
    public async Task All_stage_waits_for_everyone_sharing_the_order()
    {
        using var f = await FixtureAsync();
        var document = await ContractAsync(f, WorkflowType.Mixed,
            ("Ali", "ali@client.tn", 1, StageRule.All), ("Beya", "beya@client.tn", 1, StageRule.All), ("Chams", "chams@client.tn", 2, StageRule.All));
        var engine = f.GetHandler<IWorkflowEngine>();
        await engine.StartAsync(document.Id);

        await engine.AdvanceAsync(document.Id, (await RecipientAsync(f, "ali@client.tn")).Id);
        Assert.False(await engine.IsTurnAsync((await RecipientAsync(f, "chams@client.tn")).Id));

        await engine.AdvanceAsync(document.Id, (await RecipientAsync(f, "beya@client.tn")).Id);
        Assert.True(await engine.IsTurnAsync((await RecipientAsync(f, "chams@client.tn")).Id));

        await engine.AdvanceAsync(document.Id, (await RecipientAsync(f, "chams@client.tn")).Id);
        Assert.True(await engine.IsCompleteAsync(document.Id));
        var round = await f.Db.ContractRounds.SingleAsync(r => r.DocumentId == document.Id);
        Assert.Equal(RoundOutcome.Completed, round.Outcome);
    }

    [Fact]
    public async Task A_step_opening_on_a_Thursday_with_two_working_days_is_due_Monday_end_of_day_in_Tunis()
    {
        // Thursday 29 October 2026, 10:00 in Tunis (UTC+1).
        var opened = new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

        var due = WorkingCalendar.DueAt(opened, 2, "Africa/Tunis", []);

        Assert.Equal(new DateTime(2026, 11, 2, 22, 59, 59, DateTimeKind.Utc), due);
    }

    [Fact]
    public async Task Holidays_are_not_working_days()
    {
        // Friday 13 March 2026 + 4 working days, with Independence Day (20 March, a Friday) off: Thursday 19, then Monday 23.
        var opened = new DateTime(2026, 3, 13, 9, 0, 0, DateTimeKind.Utc);
        var holidays = OrganisationHoliday.TunisianFixed();

        var due = WorkingCalendar.DueAt(opened, 5, "Africa/Tunis", holidays);

        Assert.Equal(new DateOnly(2026, 3, 23), DateOnly.FromDateTime(due!.Value));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Handing_a_step_over_stops_the_old_link_and_the_stand_in_finishes_it()
    {
        using var f = await FixtureAsync();
        var document = await ContractAsync(f, WorkflowType.Sequential, ("Ali", "ali@client.tn", 1, StageRule.All));
        var engine = f.GetHandler<IWorkflowEngine>();
        await engine.StartAsync(document.Id);
        var aliLink = LinkOf(f, "ali@client.tn");
        var ali = await RecipientAsync(f, "ali@client.tn");

        var stand = await f.GetHandler<DelegateStepCommandHandler>().HandleAsync(
            new DelegateStepCommand(ali.Id, "Dora Juriste", "dora@client.tn", "En congé cette semaine", "127.0.0.1"));

        var refused = await Assert.ThrowsAsync<SigningRefusedException>(() =>
            f.GetHandler<ISigningAccess>().ResolveAsync(aliLink, null, SigningNeed.Link));
        Assert.Equal("link.delegated", refused.Code);
        Assert.Equal(410, refused.Status);

        var doraLink = LinkOf(f, "dora@client.tn");
        var context = await f.GetHandler<ISigningAccess>().ResolveAsync(doraLink, null, SigningNeed.Link);
        Assert.Equal(stand.RecipientId, context.Recipient.Id);
        Assert.Equal(ali.Id, context.Recipient.DelegatedFromRecipientId);

        await engine.AdvanceAsync(document.Id, stand.RecipientId);
        Assert.True(await engine.IsCompleteAsync(document.Id));
        var steps = await f.Db.WorkflowSteps.Where(s => s.WorkflowId == f.Db.Workflows.First(w => w.DocumentId == document.Id).Id).ToListAsync();
        Assert.Contains(steps, s => s.RecipientId == ali.Id && s.Status == WorkflowStepStatus.Delegated);
        Assert.Contains(steps, s => s.RecipientId == stand.RecipientId && s.DelegatedFromStepId != null && s.Status == WorkflowStepStatus.Completed);
    }

    [Fact]
    public async Task Someone_away_with_a_stand_in_has_their_step_handed_over_when_it_opens()
    {
        using var f = await FixtureAsync();
        var away = await AddUserAsync(f, "Ali", "ali@client.tn");
        var colleague = await AddUserAsync(f, "Dora", "dora@client.tn");
        var vacation = Vacation.Create(away.Id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(5));
        vacation.SetDelegate(colleague.Id);
        f.Db.Vacations.Add(vacation);
        await f.Db.SaveChangesAsync();
        var document = await ContractAsync(f, WorkflowType.Sequential, ("Ali", "ali@client.tn", 1, StageRule.All));

        await f.GetHandler<IWorkflowEngine>().StartAsync(document.Id);

        Assert.DoesNotContain(Mail(f).Sent, m => m.To == "ali@client.tn");
        var stand = await RecipientAsync(f, "dora@client.tn");
        Assert.True(await f.GetHandler<IWorkflowEngine>().IsTurnAsync(stand.Id));
        Assert.Equal(WorkflowStepStatus.Delegated, (await RecipientAsync(f, "ali@client.tn")).Status);
    }

    [Fact]
    public async Task A_team_step_is_offered_to_every_member_and_one_of_them_acts_for_it()
    {
        using var f = await FixtureAsync();
        var sami = await AddUserAsync(f, "Sami", "sami@legaldesk.tn");
        var nour = await AddUserAsync(f, "Nour", "nour@legaldesk.tn");
        var outsider = await AddUserAsync(f, "Omar", "omar@legaldesk.tn");
        var team = Team.Create("Juridique");
        f.Db.Teams.Add(team);
        f.Db.TeamMembers.AddRange(TeamMember.Of(team.Id, sami.Id), TeamMember.Of(team.Id, nour.Id));
        var document = Document.Create("Contrat cadre", "cadre.pdf", "documents/cadre.pdf", 1000, 1, OwnerId);
        f.Db.Documents.Add(document);
        await f.Db.SaveChangesAsync();

        await f.GetHandler<ManageRecipientsCommandHandler>().HandleAsync(new ManageRecipientsCommand(document.Id, WorkflowType.Sequential,
            [new RecipientInputDto(null, "", "", 1, ApprovalType.Approval, PoolTeamId: team.Id)]));
        await f.GetHandler<IWorkflowEngine>().StartAsync(document.Id);

        var pool = await f.Db.Recipients.SingleAsync(r => r.DocumentId == document.Id);
        Assert.Equal("Juridique", pool.Name);
        Assert.Equal($"/sign/session?r={pool.Id}", Mail(f).Last("sami@legaldesk.tn", "DocumentReceived", "SigningLink"));
        Assert.Contains(Mail(f).Sent, m => m.To == "nour@legaldesk.tn");

        var user = (FakeCurrentUserService)f.GetHandler<ICurrentUserService>();
        user.UserIdOverride = outsider.Id;
        user.EmailOverride = outsider.Email;
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => f.GetHandler<ISigningAccess>().OpenForAccountAsync(pool.Id, "127.0.0.1"));

        user.UserIdOverride = nour.Id;
        user.EmailOverride = nour.Email;
        var opened = await f.GetHandler<ISigningAccess>().OpenForAccountAsync(pool.Id, "127.0.0.1");
        var context = await f.GetHandler<ISigningAccess>().ResolveAsync(SigningAccess.SessionRoute, opened.Secret, SigningNeed.Act);
        Assert.Equal(nour.Id, context.Session!.UserId);

        pool.ActedBy(context.Session.UserId);
        await f.Db.SaveChangesAsync();
        await f.GetHandler<IWorkflowEngine>().AdvanceAsync(document.Id, pool.Id);
        Assert.Equal(nour.Id, (await f.Db.WorkflowSteps.SingleAsync(s => s.RecipientId == pool.Id)).ActedByUserId);
        Assert.True(await f.GetHandler<IWorkflowEngine>().IsCompleteAsync(document.Id));
    }

    [Fact]
    public async Task A_team_without_active_members_cannot_be_put_in_a_circuit()
    {
        using var f = await FixtureAsync();
        var team = Team.Create("Vide");
        f.Db.Teams.Add(team);
        var document = Document.Create("Contrat", "c.pdf", "documents/c.pdf", 1000, 1, OwnerId);
        f.Db.Documents.Add(document);
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => f.GetHandler<ManageRecipientsCommandHandler>().HandleAsync(
            new ManageRecipientsCommand(document.Id, WorkflowType.Sequential, [new RecipientInputDto(null, "", "", 1, ApprovalType.Approval, PoolTeamId: team.Id)])));
        Assert.Equal("circuit.poolEmpty", ex.Code);
    }

    [Fact]
    public async Task Open_steps_are_reminded_on_schedule_and_escalated_once_late()
    {
        using var f = await FixtureAsync();
        var document = Document.Create("Contrat", "c.pdf", "documents/c.pdf", 1000, 1, OwnerId);
        f.Db.Documents.Add(document);
        var ali = Recipient.Create(document.Id, "Ali", "ali@client.tn", 1, ApprovalType.Approval);
        ali.SetCircuit(StageRule.All, null, null, 3, 2, 4, "direction@legaldesk.tn");
        f.Db.Recipients.Add(ali);
        await f.Db.SaveChangesAsync();
        var engine = f.GetHandler<IWorkflowEngine>();
        await engine.StartAsync(document.Id);
        var step = await f.Db.WorkflowSteps.SingleAsync(s => s.RecipientId == ali.Id);
        Assert.NotNull(step.DueAt);

        Assert.Equal(0, await engine.FollowUpAsync(DateTime.UtcNow.AddDays(1)));
        Assert.Equal(1, await engine.FollowUpAsync(DateTime.UtcNow.AddDays(2.1)));
        Assert.Equal(1, step.ReminderCount);
        Assert.Contains(Mail(f).Sent, m => m.To == "ali@client.tn" && m.Template == "Reminder");

        await engine.FollowUpAsync(DateTime.UtcNow.AddDays(4.1));
        Assert.NotNull(step.EscalatedAt);
        Assert.Single(Mail(f).Sent, m => m.To == "direction@legaldesk.tn" && m.Template == "StepEscalated");
        await engine.FollowUpAsync(DateTime.UtcNow.AddDays(4.2));
        Assert.Single(Mail(f).Sent, m => m.To == "direction@legaldesk.tn" && m.Template == "StepEscalated");
    }

    [Fact]
    public async Task A_revision_after_changes_keeps_round_one_and_the_next_send_opens_round_two_without_the_stand_in()
    {
        using var f = await FixtureAsync();
        var document = await ContractAsync(f, WorkflowType.Sequential, ("Ali", "ali@client.tn", 1, StageRule.All));
        var engine = f.GetHandler<IWorkflowEngine>();
        await engine.StartAsync(document.Id);
        var ali = await RecipientAsync(f, "ali@client.tn");
        var stand = await engine.DelegateAsync(document.Id, ali.Id, "Dora", "dora@client.tn", "Absent");
        await engine.RequestChangesAsync(document.Id, stand.Id, "Article 4 à revoir");

        // The owner revises the contract, as UploadRevisionCommand does.
        var loaded = await f.Db.Documents.Include(d => d.Recipients).Include(d => d.Workflow).FirstAsync(d => d.Id == document.Id);
        await CircuitRounds.CloseForRevisionAsync(f.Db, loaded);
        foreach (var r in loaded.Recipients) r.ResetForRevision();
        loaded.ApplyRevision("c2.pdf", "documents/c2.pdf", 1000, 1, "hash");
        await f.Db.SaveChangesAsync();
        await engine.StartAsync(document.Id);

        var rounds = await f.Db.ContractRounds.Where(r => r.DocumentId == document.Id).OrderBy(r => r.Number).ToListAsync();
        Assert.Equal([RoundOutcome.ChangesRequested, RoundOutcome.Open], rounds.Select(r => r.Outcome));
        Assert.Equal(2, rounds[1].VersionNumber);
        Assert.Equal(3, await f.Db.WorkflowSteps.CountAsync()); // Ali's and Dora's round-1 steps, Ali's round-2 step
        Assert.True(await engine.IsTurnAsync(ali.Id));
        Assert.False(await engine.IsTurnAsync(stand.Id));
        Assert.NotNull((await f.Db.Recipients.FindAsync(stand.Id))!.RetiredAt);
    }

    [Fact]
    public async Task With_the_mail_server_down_the_circuit_still_starts_and_its_mail_waits_in_the_outbox()
    {
        using var f = await FixtureAsync();
        var document = await ContractAsync(f, WorkflowType.Sequential, ("Ali", "ali@client.tn", 1, StageRule.All));
        // The production e-mail service only writes to the outbox; the dispatcher sends and retries later (R-M4-9).
        var engine = new WorkflowEngine(f.Db, new OutboxEmailService(f.Db), f.GetHandler<INotificationHub>(),
            f.GetHandler<INotificationService>(), NullLogger<WorkflowEngine>.Instance);

        await engine.StartAsync(document.Id);

        Assert.Equal(DocumentStatus.InProgress, (await f.Db.Documents.FindAsync(document.Id))!.Status);
        Assert.Contains(await f.Db.OutboxMessages.ToListAsync(), m => m.SentAt == null && m.PayloadJson.Contains("ali@client.tn"));
    }
}
