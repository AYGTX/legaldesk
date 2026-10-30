using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Common.Security;
using SignFlow.Application.Features.Auth;
using SignFlow.Application.Features.Auth.Commands;
using SignFlow.Domain.Entities;
using SignFlow.Infrastructure.Extensions;
using SignFlow.Infrastructure.Persistence;
using SignFlow.Infrastructure.Services.Auth;
using SignFlow.Infrastructure.Services.Email;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// Rework spec M1 acceptance: sign-in answers the same for every failure, sessions rotate and detect replays,
/// two-factor works with an authenticator code or a single-use recovery code, and invitations are the only way in.
/// </summary>
public sealed class IdentityTests : IDisposable
{
    private static readonly Guid Org = Organisation.DefaultId;
    private static readonly ClientInfo Client = new("10.0.0.1", "tests");
    private const string Password = "Correct-horse-9";

    private readonly ServiceProvider _root;
    private readonly FakeCurrentUserService _currentUser = new(Guid.Empty, "admin@test.tn");

    public IdentityTests()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IPlatformQueries, PlatformQueries>();
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        services.AddDataProtection();
        services.Configure<JwtSettings>(o =>
        {
            o.Secret = new string('k', 48);
            o.Issuer = "test";
            o.Audience = "test";
            o.ExpirationMinutes = 15;
        });
        services.Configure<SecurityOptions>(o => o.PublicUrl = "https://app.test");
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IEmailService, OutboxEmailService>();
        services.AddSingleton<ICurrentUserService>(_currentUser);
        services.AddSingleton<IQrCodes, SignFlow.Infrastructure.Services.Pdf.QrCodes>();
        services.AddScoped<AuthSessions>();
        services.AddScoped<PasswordResetMail>();
        services.AddScoped<LoginCommandHandler>();
        services.AddScoped<MfaLoginCommandHandler>();
        services.AddScoped<RefreshTokenCommandHandler>();
        services.AddScoped<LogoutCommandHandler>();
        services.AddScoped<InviteUserCommandHandler>();
        services.AddScoped<AcceptInvitationCommandHandler>();
        services.AddScoped<BeginMfaSetupCommandHandler>();
        services.AddScoped<ConfirmMfaSetupCommandHandler>();
        services.AddScoped<ResetPasswordCommandHandler>();
        _root = services.BuildServiceProvider();

        using var scope = _root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Organisations.Add(Organisation.Create("Org", "org", Org));
        db.SaveChanges();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "Administrator", "Manager", "Employee" })
            roles.CreateAsync(new IdentityRole<Guid>(role)).GetAwaiter().GetResult();
    }

    public void Dispose() => _root.Dispose();

    /// <summary>A request-sized scope acting for the organisation, as a signed-in request would.</summary>
    private AsyncServiceScope Scope(bool scoped = true)
    {
        var scope = _root.CreateAsyncScope();
        if (scoped) scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(Org);
        return scope;
    }

    private async Task<ApplicationUser> CreateUser(string email, string role = "Employee", bool active = true)
    {
        await using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, FirstName = "A", LastName = "B", IsActive = active };
        user.AssignOrganisation(Org);
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        await users.AddToRoleAsync(user, role);
        return user;
    }

    private async Task<LoginOutcome> Login(string email, string password)
    {
        await using var scope = Scope(scoped: false); // sign-in is anonymous
        return await scope.ServiceProvider.GetRequiredService<LoginCommandHandler>()
            .HandleAsync(new LoginCommand(email, password, Client));
    }

    // ------------------------------------------------------------------ sign-in

    [Fact]
    public async Task Every_failed_sign_in_gets_the_same_answer()
    {
        await CreateUser("known@test.tn");
        await CreateUser("off@test.tn", active: false);

        var unknown = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Login("nobody@test.tn", Password));
        var wrong = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Login("known@test.tn", "Wrong-pass-123"));
        var inactive = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Login("off@test.tn", Password));

        Assert.All(new[] { unknown, wrong, inactive }, e => Assert.Equal(LoginCommandHandler.InvalidCredentials, e.Message));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_with_the_same_answer()
    {
        await CreateUser("locked@test.tn");
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Login("locked@test.tn", "Wrong-pass-123"));

        // Even the right password is refused now, with the same words.
        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Login("locked@test.tn", Password));
        Assert.Equal(LoginCommandHandler.InvalidCredentials, error.Message);
    }

    // ------------------------------------------------------------------ sessions

    [Fact]
    public async Task A_session_stores_only_the_hash_and_rotates_on_refresh()
    {
        await CreateUser("s@test.tn");
        var first = (await Login("s@test.tn", Password)).Session!;

        await using (var scope = Scope(scoped: false))
        {
            var stored = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserSessions.SingleAsync();
            Assert.Equal(SecureTokens.Hash(first.RefreshToken), stored.TokenHash);
            Assert.DoesNotContain(first.RefreshToken, JsonSerializer.Serialize(stored));
        }

        var second = await Refresh(first.RefreshToken);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotNull(await Refresh(second.RefreshToken));
    }

    [Fact]
    public async Task Replaying_an_old_refresh_token_ends_the_session()
    {
        await CreateUser("r@test.tn");
        var stolen = (await Login("r@test.tn", Password)).Session!.RefreshToken;
        var current = (await Refresh(stolen)).RefreshToken;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Refresh(stolen));
        // The legitimate holder is signed out too: the session is gone for everyone.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Refresh(current));
    }

    [Fact]
    public async Task Logout_and_password_reset_end_sessions()
    {
        var user = await CreateUser("l@test.tn");
        var a = (await Login("l@test.tn", Password)).Session!.RefreshToken;
        var b = (await Login("l@test.tn", Password)).Session!.RefreshToken;

        await using (var scope = Scope(scoped: false))
            await scope.ServiceProvider.GetRequiredService<LogoutCommandHandler>().HandleAsync(new LogoutCommand(a));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Refresh(a));
        Assert.NotNull(await Refresh(b));

        string token;
        await using (var scope = Scope())
            token = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GeneratePasswordResetTokenAsync(user);
        await using (var scope = Scope(scoped: false))
            await scope.ServiceProvider.GetRequiredService<ResetPasswordCommandHandler>()
                .HandleAsync(new ResetPasswordCommand("l@test.tn", token, "Another-pass-42"));

        var reused = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            var latest = await Refresh(b);
            await Refresh(latest.RefreshToken);
        });
        Assert.NotNull(reused);
    }

    private async Task<AuthSession> Refresh(string token)
    {
        await using var scope = Scope(scoped: false);
        return await scope.ServiceProvider.GetRequiredService<RefreshTokenCommandHandler>()
            .HandleAsync(new RefreshTokenCommand(token, Client));
    }

    // ------------------------------------------------------------------ two-factor

    [Fact]
    public async Task An_administrator_without_two_factor_is_told_to_set_it_up()
    {
        await CreateUser("boss@test.tn", "Administrator");
        var session = (await Login("boss@test.tn", Password)).Session!;
        Assert.True(session.User.MfaSetupRequired);

        await CreateUser("staff@test.tn");
        Assert.False((await Login("staff@test.tn", Password)).Session!.User.MfaSetupRequired);
    }

    [Fact]
    public async Task Two_factor_setup_then_sign_in_with_a_code_or_a_recovery_code_once()
    {
        var user = await CreateUser("mfa@test.tn", "Manager");
        _currentUser.UserIdOverride = user.Id;

        string key;
        IReadOnlyList<string> recovery;
        await using (var scope = Scope())
        {
            var setup = await scope.ServiceProvider.GetRequiredService<BeginMfaSetupCommandHandler>()
                .HandleAsync(new BeginMfaSetupCommand());
            Assert.StartsWith("data:image/png;base64,", setup.QrCodeDataUri);
            key = setup.SharedKey;
            recovery = await scope.ServiceProvider.GetRequiredService<ConfirmMfaSetupCommandHandler>()
                .HandleAsync(new ConfirmMfaSetupCommand(Totp(key)));
        }
        Assert.Equal(10, recovery.Count);

        var outcome = await Login("mfa@test.tn", Password);
        Assert.Null(outcome.Session);
        Assert.NotNull(outcome.MfaChallenge);

        Assert.False((await MfaLogin(outcome.MfaChallenge!, Totp(key))).User.MfaSetupRequired);

        var again = (await Login("mfa@test.tn", Password)).MfaChallenge!;
        Assert.NotNull(await MfaLogin(again, recovery[0]));
        var third = (await Login("mfa@test.tn", Password)).MfaChallenge!;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => MfaLogin(third, recovery[0]));
    }

    [Fact]
    public async Task A_challenge_is_not_an_access_token_and_a_forged_one_is_refused()
    {
        await using var scope = Scope(scoped: false);
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = await CreateUser("c@test.tn");

        Assert.False(tokens.ValidateToken(tokens.GenerateMfaChallenge(user)));
        Assert.Null(tokens.ValidateMfaChallenge(tokens.GenerateAccessToken(user, ["Employee"])));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => MfaLogin("forged", "123456"));
    }

    private async Task<AuthSession> MfaLogin(string challenge, string code)
    {
        await using var scope = Scope(scoped: false);
        return await scope.ServiceProvider.GetRequiredService<MfaLoginCommandHandler>()
            .HandleAsync(new MfaLoginCommand(challenge, code, Client));
    }

    /// <summary>RFC 6238 code for the current 30-second step, as an authenticator app computes it.</summary>
    private static string Totp(string base32Key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = base32Key.Replace(" ", "").TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8) { bytes.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counter);
        var offset = hash[^1] & 0x0F;
        var value = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    // ------------------------------------------------------------------ invitations

    [Fact]
    public async Task An_invitation_creates_the_account_once_in_the_inviting_organisation()
    {
        var admin = await CreateUser("admin@test.tn", "Administrator");
        _currentUser.UserIdOverride = admin.Id;
        _currentUser.RolesOverride = ["Administrator"];

        await using (var scope = Scope())
            await scope.ServiceProvider.GetRequiredService<InviteUserCommandHandler>()
                .HandleAsync(new InviteUserCommand("new@test.tn", "Nadia", "Ben Ali", "Manager"));

        string token;
        await using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var mail = await db.OutboxMessages.SingleAsync();
            var link = JsonSerializer.Deserialize<EmailMessage>(mail.PayloadJson, OutboxEmailService.Json)!.HtmlBody;
            token = Uri.UnescapeDataString(link.Split("token=")[1].Split('\\', '"', '&', '<')[0]);
            var stored = await db.UserInvitations.SingleAsync();
            Assert.Equal(SecureTokens.Hash(token), stored.TokenHash);
        }

        AuthSession session;
        await using (var scope = Scope(scoped: false))
            session = await scope.ServiceProvider.GetRequiredService<AcceptInvitationCommandHandler>()
                .HandleAsync(new AcceptInvitationCommand(token, "Brand-new-pass-1", Client));
        Assert.Contains("Manager", session.User.Roles);

        await using (var scope = Scope(scoped: false))
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("new@test.tn");
            Assert.Equal(Org, user!.OrganisationId);
            await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider.GetRequiredService<AcceptInvitationCommandHandler>()
                .HandleAsync(new AcceptInvitationCommand(token, "Brand-new-pass-1", Client)));
        }
    }

    [Fact]
    public async Task Inviting_an_address_that_already_has_an_account_is_refused()
    {
        var admin = await CreateUser("admin2@test.tn", "Administrator");
        await CreateUser("taken@test.tn");
        _currentUser.UserIdOverride = admin.Id;
        _currentUser.RolesOverride = ["Administrator"];

        await using var scope = Scope();
        await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<InviteUserCommandHandler>()
            .HandleAsync(new InviteUserCommand("taken@test.tn", "T", "U", "Employee")));
    }

    [Fact]
    public async Task Only_an_administrator_can_invite()
    {
        _currentUser.RolesOverride = ["Manager"];
        await using var scope = Scope();
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => scope.ServiceProvider.GetRequiredService<InviteUserCommandHandler>()
            .HandleAsync(new InviteUserCommand("x@test.tn", "X", "Y", "Employee")));
    }
}
