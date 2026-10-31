using System.ComponentModel.DataAnnotations;

namespace SignFlow.Domain.Entities;

/// <summary>
/// A 6-digit code e-mailed to a participant before their first action (R-M5-3): valid 10 minutes, 5 tries, only its
/// hash is kept.
/// </summary>
public class SigningOtp : TenantEntity
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public Guid RecipientId { get; private set; }

    [MaxLength(64)]
    public string CodeHash { get; private set; } = string.Empty;

    public DateTime ExpiresAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? UsedAt { get; private set; }

    private SigningOtp() { }

    public static SigningOtp Issue(Guid recipientId, string codeHash) => new()
    {
        RecipientId = recipientId,
        CodeHash = codeHash,
        ExpiresAt = DateTime.UtcNow.Add(Lifetime)
    };

    public bool IsUsable => UsedAt is null && Attempts < MaxAttempts && ExpiresAt > DateTime.UtcNow;

    /// <summary>One try. True when the code matches; a wrong code counts against the five.</summary>
    public bool Try(string codeHash)
    {
        if (!IsUsable) return false;
        Attempts++;
        if (!string.Equals(codeHash, CodeHash, StringComparison.Ordinal)) return false;
        UsedAt = DateTime.UtcNow;
        return true;
    }
}

/// <summary>
/// What lets a participant act (R-M5-4): opened by the e-mailed code, or by their LegalDesk account; lasts 30 minutes
/// and is renewed by activity. The link alone never acts.
/// </summary>
public class SigningSession : TenantEntity
{
    public const string EmailCode = "EmailCode";
    public const string Account = "Account";
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);

    public Guid RecipientId { get; private set; }

    [MaxLength(64)]
    public string SecretHash { get; private set; } = string.Empty;

    [MaxLength(32)]
    public string Method { get; private set; } = EmailCode;

    [MaxLength(256)]
    public string VerifiedEmail { get; private set; } = string.Empty;

    public DateTime VerifiedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    [MaxLength(64)]
    public string? Ip { get; private set; }

    private SigningSession() { }

    public static SigningSession Open(Guid recipientId, string secretHash, string method, string verifiedEmail, string? ip) => new()
    {
        RecipientId = recipientId,
        SecretHash = secretHash,
        Method = method,
        VerifiedEmail = verifiedEmail,
        VerifiedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.Add(IdleLifetime),
        Ip = ip
    };

    public bool IsExpired => ExpiresAt <= DateTime.UtcNow;

    /// <summary>Activity keeps the session open for another 30 minutes.</summary>
    public void Touch()
    {
        var next = DateTime.UtcNow.Add(IdleLifetime);
        // Avoid a database write on every call: renew once a minute at most.
        if (next - ExpiresAt > TimeSpan.FromMinutes(1))
        {
            ExpiresAt = next;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
