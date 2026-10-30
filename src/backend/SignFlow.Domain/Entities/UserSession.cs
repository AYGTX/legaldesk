using System.ComponentModel.DataAnnotations;

namespace SignFlow.Domain.Entities;

/// <summary>
/// One signed-in device (rework spec R-M1-4). The refresh token is never stored, only its SHA-256; it rotates on
/// every use, and presenting the previous one again means it was stolen, so the whole session ends.
/// </summary>
public class UserSession : TenantEntity
{
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromDays(14);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(90);

    public Guid UserId { get; private set; }

    [MaxLength(64)]
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>The hash this session had before its last rotation; seeing it again is a replay.</summary>
    [MaxLength(64)]
    public string? PreviousTokenHash { get; private set; }

    public DateTime LastUsedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    [MaxLength(64)]
    public string? IpAddress { get; private set; }

    [MaxLength(512)]
    public string? UserAgent { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    [MaxLength(40)]
    public string? RevokedReason { get; private set; }

    private UserSession() { }

    public static UserSession Start(ApplicationUser user, string tokenHash, string? ip, string? userAgent, DateTime now)
    {
        var session = new UserSession
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            LastUsedAt = now,
            ExpiresAt = now + AbsoluteLifetime,
            IpAddress = ip,
            UserAgent = userAgent is { Length: > 512 } ? userAgent[..512] : userAgent,
            CreatedAt = now
        };
        // Sign-in is anonymous (no organisation in scope yet): the session belongs to the user's organisation.
        session.AssignOrganisation(user.OrganisationId);
        return session;
    }

    public bool IsActive(DateTime now) =>
        RevokedAt is null && now < ExpiresAt && now - LastUsedAt < IdleLifetime;

    public void Rotate(string newTokenHash, string? ip, DateTime now)
    {
        PreviousTokenHash = TokenHash;
        TokenHash = newTokenHash;
        LastUsedAt = now;
        IpAddress = ip ?? IpAddress;
        UpdatedAt = now;
    }

    public void Revoke(string reason, DateTime now)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        RevokedReason = reason;
        UpdatedAt = now;
    }
}
