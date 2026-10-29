using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Infrastructure.Persistence;
using SignFlow.Infrastructure.Services.Email;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// R-M4-9: an action only queues its e-mail; the dispatcher sends it, retries on failure with growing delays, and
/// gives up after five attempts without ever sending a message twice.
/// </summary>
public class OutboxTests : IDisposable
{
    private static readonly Guid Org = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly List<ApplicationDbContext> _contexts = [];

    private sealed class FakeSmtp : ISmtpSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool Down { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (Down) throw new System.Net.Sockets.SocketException(10061);
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private ApplicationDbContext Context()
    {
        var tenant = new TenantContext();
        tenant.BeginScope(Org);
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options, tenant);
        _contexts.Add(db);
        return db;
    }

    public void Dispose() => _contexts.ForEach(c => c.Dispose());

    private async Task Queue(string to)
    {
        var db = Context();
        if (!await db.Organisations.AnyAsync())
        {
            db.Organisations.Add(Organisation.Create("Org", "org", Org));
            await db.SaveChangesAsync();
        }
        await new OutboxEmailService(db).SendTemplateAsync(to, "Completed", new { Title = "Lease" });
    }

    private async Task Dispatch(FakeSmtp smtp)
    {
        var db = Context();
        var job = new OutboxDispatcherJob(db, db.Tenant, new PlatformQueries(db), smtp, NullLogger<OutboxDispatcherJob>.Instance);
        await job.DispatchAsync();
    }

    [Fact]
    public async Task Queuing_an_email_sends_nothing_until_the_dispatcher_runs()
    {
        var smtp = new FakeSmtp();
        await Queue("owner@test.tn");
        Assert.Empty(smtp.Sent);

        await Dispatch(smtp);

        var sent = Assert.Single(smtp.Sent);
        Assert.Equal("owner@test.tn", sent.To);
        Assert.NotNull((await Context().OutboxMessages.SingleAsync()).SentAt);
    }

    [Fact]
    public async Task A_sent_message_is_never_sent_twice()
    {
        var smtp = new FakeSmtp();
        await Queue("owner@test.tn");

        await Dispatch(smtp);
        await Dispatch(smtp);

        Assert.Single(smtp.Sent);
    }

    [Fact]
    public async Task A_failure_is_recorded_and_retried_later_not_immediately()
    {
        var smtp = new FakeSmtp { Down = true };
        await Queue("owner@test.tn");

        await Dispatch(smtp);
        var message = await Context().OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(1, message.Attempts);
        Assert.Null(message.SentAt);
        Assert.Contains("SocketException", message.LastError);
        Assert.True(message.NextAttemptAt > DateTime.UtcNow.AddSeconds(50));

        // Back up, but the retry is not due yet: nothing goes out.
        smtp.Down = false;
        await Dispatch(smtp);
        Assert.Empty(smtp.Sent);
    }

    [Fact]
    public void After_the_fifth_failure_the_message_is_given_up()
    {
        var message = OutboxMessage.Create(OutboxMessage.EmailKind, "{}", DateTime.UtcNow);
        var now = DateTime.UtcNow;

        for (var i = 1; i < OutboxMessage.MaxAttempts; i++)
        {
            message.MarkFailed("down", now);
            Assert.False(message.GaveUp);
        }
        message.MarkFailed("down", now);

        Assert.Equal(5, message.Attempts);
        Assert.True(message.GaveUp);
        Assert.Null(message.NextAttemptAt);
    }

    [Fact]
    public async Task Each_organisation_only_dispatches_its_own_messages()
    {
        var smtp = new FakeSmtp();
        await Queue("owner@test.tn");

        // A context acting for another organisation sees no message to send.
        var other = Context();
        using (other.Tenant.BeginScope(Guid.NewGuid()))
            Assert.Empty(await other.OutboxMessages.ToListAsync());
    }
}
