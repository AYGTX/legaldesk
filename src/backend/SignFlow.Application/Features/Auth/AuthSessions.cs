using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Common.Security;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Auth;

public record UserInfo(Guid Id, string Email, string FirstName, string LastName, string FullName, IList<string> Roles,
    string? AvatarUrl, bool MfaEnabled, string? Department, string? Company = null, bool MfaSetupRequired = false);

/// <summary>A signed-in device: the access token goes in the response body, the refresh token only in a cookie.</summary>
public record AuthSession(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken,
    DateTime SessionExpiresAt, UserInfo User);

/// <summary>What a password sign-in produced: a session, or a two-factor challenge to answer first.</summary>
public record LoginOutcome(AuthSession? Session, string? MfaChallenge)
{
    public static LoginOutcome Signed(AuthSession session) => new(session, null);
    public static LoginOutcome NeedsMfa(string challenge) => new(null, challenge);
}

/// <summary>Who is signing in from where, read from the request by the controller.</summary>
public record ClientInfo(string? IpAddress, string? UserAgent);

/// <summary>
/// Starts, renews and ends sessions (R-M1-4). Refresh tokens are 32 random bytes, stored as SHA-256 and rotated on
/// every use; presenting the token a session had before its last rotation is a replay, so the session ends.
/// </summary>
public class AuthSessions(
    IApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ITokenService tokens,
    IOptions<SecurityOptions> security)
{
    public static readonly string[] PrivilegedRoles = [Permissions.Administrator, Permissions.Manager];

    public async Task<AuthSession> StartAsync(ApplicationUser user, ClientInfo client, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var refreshToken = SecureTokens.Generate();
        var session = UserSession.Start(user, SecureTokens.Hash(refreshToken), client.IpAddress, client.UserAgent, now);
        db.UserSessions.Add(session);

        user.LastLoginAt = now;
        await userManager.UpdateAsync(user); // saves the session too: same context
        await db.SaveChangesAsync(ct);

        return await BuildAsync(user, session, refreshToken, now);
    }

    public async Task<AuthSession> RefreshAsync(string refreshToken, ClientInfo client, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new UnauthorizedAccessException("Session expired.");

        var now = DateTime.UtcNow;
        var hash = SecureTokens.Hash(refreshToken);
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);

        if (session is null)
        {
            // An old token of a live session: someone kept a copy. End that session for everyone holding it.
            var replayed = await db.UserSessions.FirstOrDefaultAsync(s => s.PreviousTokenHash == hash, ct);
            if (replayed is not null)
            {
                replayed.Revoke("replay", now);
                await db.SaveChangesAsync(ct);
            }
            throw new UnauthorizedAccessException("Session expired.");
        }

        if (!session.IsActive(now))
            throw new UnauthorizedAccessException("Session expired.");

        var user = await userManager.FindByIdAsync(session.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            session.Revoke("user-inactive", now);
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("Session expired.");
        }

        var newToken = SecureTokens.Generate();
        session.Rotate(SecureTokens.Hash(newToken), client.IpAddress, now);
        await db.SaveChangesAsync(ct);

        return await BuildAsync(user, session, newToken, now);
    }

    public async Task EndAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = SecureTokens.Hash(refreshToken);
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is null) return;
        session.Revoke("logout", DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Ends every session of a user: after a password reset or change, or when an account is deactivated.</summary>
    public async Task EndAllAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var open = await db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync(ct);
        foreach (var session in open)
            session.Revoke(reason, now);
        await db.SaveChangesAsync(ct);
    }

    public async Task<UserInfo> DescribeAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new UserInfo(user.Id, user.Email!, user.FirstName, user.LastName, user.FullName, roles, user.AvatarUrl,
            user.TwoFactorEnabled, user.Department, user.Company, MfaSetupRequired(user, roles));
    }

    private bool MfaSetupRequired(ApplicationUser user, IList<string> roles) =>
        security.Value.RequireMfaForPrivilegedRoles
        && !user.TwoFactorEnabled
        && roles.Any(r => PrivilegedRoles.Contains(r));

    private async Task<AuthSession> BuildAsync(ApplicationUser user, UserSession session, string refreshToken, DateTime now)
    {
        var info = await DescribeAsync(user);
        var access = tokens.GenerateAccessToken(user, info.Roles, info.MfaSetupRequired);
        var idleEnd = now + UserSession.IdleLifetime;
        return new AuthSession(access, now + tokens.AccessTokenLifetime, refreshToken,
            idleEnd < session.ExpiresAt ? idleEnd : session.ExpiresAt, info);
    }
}
