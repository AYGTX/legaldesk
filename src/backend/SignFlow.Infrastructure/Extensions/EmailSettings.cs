namespace SignFlow.Infrastructure.Extensions;

public class EmailSettings
{
    /// <summary>When false, SendAsync/SendTemplateAsync are no-ops (logged, not sent) — lets dev toggle real email delivery on/off without touching SMTP config.</summary>
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromEmail { get; set; } = "noreply@signflow.app";
    public string FromName { get; set; } = "SignFlow";
}
