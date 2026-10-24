using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Domain.Entities;
using SignFlow.Infrastructure.Persistence;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// A deployment can switch the three built-in demo accounts off and list its own people instead. The reference
/// data (clause library, regulatory registry) must still be seeded, attributed to the first real administrator.
/// </summary>
public class ConfiguredUsersSeedingTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ApplicationDbContext _db = null!;

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>(o => o.Password.RequiredLength = 8)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<ApplicationDbContext>();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() { _provider.Dispose(); return Task.CompletedTask; }

    private Task SeedAsync(SeedUsersOptions options) => DatabaseSeeder.SeedAsync(
        _db, _provider.GetRequiredService<UserManager<ApplicationUser>>(),
        _provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(), null, NullLogger.Instance, options);

    private static SeedUser Person(string email, string role, string password = "Str0ng#Pass") =>
        new(email, email.Split('@')[0], role, role, password, "ACME", "Legal");

    [Fact]
    public async Task Only_the_listed_people_exist_when_the_built_in_accounts_are_off()
    {
        await SeedAsync(new SeedUsersOptions(false, [Person("a@x.tn", "Administrator"), Person("m@x.tn", "Manager")]));

        var emails = await _db.Users.Select(u => u.Email).ToListAsync();
        Assert.Equal(["a@x.tn", "m@x.tn"], emails.OrderBy(e => e));
        Assert.DoesNotContain("admin@legaldesk.tn", emails);
    }

    [Fact]
    public async Task Reference_data_is_still_seeded_and_owned_by_the_first_real_administrator()
    {
        await SeedAsync(new SeedUsersOptions(false, [Person("boss@x.tn", "Administrator")]));

        var boss = await _db.Users.SingleAsync(u => u.Email == "boss@x.tn");
        Assert.NotEmpty(_db.Clauses);
        Assert.All(_db.Clauses, c => Assert.Equal(boss.Id, c.CreatedById));
        Assert.NotEmpty(_db.RegulatoryTexts);
    }

    [Fact]
    public async Task The_built_in_accounts_are_still_created_by_default()
    {
        await SeedAsync(new SeedUsersOptions(true, []));
        Assert.Equal(3, await _db.Users.CountAsync());
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_people()
    {
        var options = new SeedUsersOptions(false, [Person("a@x.tn", "Administrator")]);
        await SeedAsync(options);
        await SeedAsync(options);
        Assert.Equal(1, await _db.Users.CountAsync());
    }

    [Fact]
    public async Task A_weak_password_stops_the_start_up_instead_of_being_skipped()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SeedAsync(new SeedUsersOptions(false, [Person("a@x.tn", "Administrator", password: "abc")])));
        Assert.Contains("a@x.tn", ex.Message);
    }

    [Fact]
    public async Task An_unknown_role_stops_the_start_up()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SeedAsync(new SeedUsersOptions(false, [Person("a@x.tn", "Wizard")])));
        Assert.Contains("Wizard", ex.Message);
    }
}
