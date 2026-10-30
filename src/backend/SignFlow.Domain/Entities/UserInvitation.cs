using System.ComponentModel.DataAnnotations;

namespace SignFlow.Domain.Entities;

/// <summary>
/// An Administrator's invitation to join the organisation (R-M1-1): accounts exist only this way. The link's token is
/// shown once in the e-mail and stored as SHA-256; it works once, for 72 hours.
/// </summary>
public class UserInvitation : TenantEntity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);

    [MaxLength(256)]
    public string Email { get; private set; } = string.Empty;

    [MaxLength(100)]
    public string FirstName { get; private set; } = string.Empty;

    [MaxLength(100)]
    public string LastName { get; private set; } = string.Empty;

    [MaxLength(50)]
    public string Role { get; private set; } = string.Empty;

    [MaxLength(64)]
    public string TokenHash { get; private set; } = string.Empty;

    public DateTime ExpiresAt { get; private set; }
    public Guid InvitedByUserId { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public Guid? AcceptedUserId { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    private UserInvitation() { }

    public static UserInvitation Create(string email, string firstName, string lastName, string role, string tokenHash,
        Guid invitedBy, DateTime now) => new()
    {
        Email = email.Trim(),
        FirstName = firstName.Trim(),
        LastName = lastName.Trim(),
        Role = role,
        TokenHash = tokenHash,
        InvitedByUserId = invitedBy,
        ExpiresAt = now + Lifetime,
        CreatedAt = now
    };

    public bool IsOpen(DateTime now) => AcceptedAt is null && RevokedAt is null && now < ExpiresAt;

    public void Accept(Guid userId, DateTime now)
    {
        AcceptedAt = now;
        AcceptedUserId = userId;
        UpdatedAt = now;
    }

    public void Revoke(DateTime now)
    {
        RevokedAt ??= now;
        UpdatedAt = now;
    }
}
