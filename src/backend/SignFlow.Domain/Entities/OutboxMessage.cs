using System.ComponentModel.DataAnnotations;

namespace SignFlow.Domain.Entities;

/// <summary>
/// Something to send after the action that caused it has been saved (rework spec R-M4-9). The action never waits
/// on, or fails because of, the mail server: a dispatcher job sends due messages and retries failures.
/// </summary>
public class OutboxMessage : TenantEntity
{
    public const string EmailKind = "Email";

    /// <summary>Minutes to wait after each failed attempt; after the last one the message is given up.</summary>
    private static readonly int[] RetryDelaysMinutes = [1, 5, 15, 60, 240];

    public static int MaxAttempts => RetryDelaysMinutes.Length;

    [MaxLength(40)]
    public string Kind { get; private set; } = EmailKind;

    public string PayloadJson { get; private set; } = string.Empty;

    public int Attempts { get; private set; }

    /// <summary>When the dispatcher may try next; null once sent or given up.</summary>
    public DateTime? NextAttemptAt { get; private set; }

    public DateTime? SentAt { get; private set; }

    [MaxLength(2000)]
    public string? LastError { get; private set; }

    public bool GaveUp => SentAt is null && NextAttemptAt is null;

    private OutboxMessage() { }

    public static OutboxMessage Create(string kind, string payloadJson, DateTime now) => new()
    {
        Kind = kind,
        PayloadJson = payloadJson,
        NextAttemptAt = now
    };

    public void MarkSent(DateTime now)
    {
        Attempts++;
        SentAt = now;
        NextAttemptAt = null;
        LastError = null;
        UpdatedAt = now;
    }

    public void MarkFailed(string error, DateTime now)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        NextAttemptAt = Attempts >= MaxAttempts ? null : now.AddMinutes(RetryDelaysMinutes[Attempts - 1]);
        UpdatedAt = now;
    }
}
