using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Infrastructure.Extensions;

namespace SignFlow.Infrastructure.Services.Email;

public class MailKitEmailService(IOptions<EmailSettings> settings, ILogger<MailKitEmailService> logger) : IEmailService
{
    private readonly EmailSettings _settings = settings.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            logger.LogInformation("Email sending is disabled (Email:Enabled=false). Skipping email to {To} with subject {Subject}", message.To, message.Subject);
            return;
        }

        var email = new MimeMessage();
        email.From.Add(new MailboxAddress(message.FromName ?? _settings.FromName, message.From ?? _settings.FromEmail));
        email.To.Add(MailboxAddress.Parse(message.To));
        email.Subject = message.Subject;

        var bodyBuilder = new BodyBuilder { HtmlBody = message.HtmlBody };

        if (message.Attachments is not null)
        {
            foreach (var attachment in message.Attachments)
            {
                bodyBuilder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
            }
        }

        email.Body = bodyBuilder.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_settings.Host, _settings.Port, _settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);

            if (!string.IsNullOrEmpty(_settings.Username))
                await client.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);

            await client.SendAsync(email, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("Email sent to {To} with subject {Subject}", message.To, message.Subject);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or MailKit.Security.AuthenticationException or MailKit.CommandException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Email to {To} could not be delivered (SMTP unavailable). Skipping.", message.To);
        }
    }

    public Task SendTemplateAsync(string to, string templateName, object model, string? subject = null, CancellationToken cancellationToken = default)
    {
        // Basic template rendering — in production use a proper template engine (Fluid, Scriban, etc.)
        var body = BuildTemplateBody(templateName, model);
        var finalSubject = subject ?? GetDefaultSubject(templateName);

        return SendAsync(new EmailMessage(to, finalSubject, body), cancellationToken);
    }

    private static string BuildTemplateBody(string templateName, object model)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(model);
        return $"""
            <html><body>
            <h2>{templateName}</h2>
            <p>Model: {System.Web.HttpUtility.HtmlEncode(json)}</p>
            </body></html>
            """;
    }

    private static string GetDefaultSubject(string templateName) => templateName switch
    {
        "DocumentReceived" => "Please sign your document",
        "Reminder" => "Reminder: Document awaiting your signature",
        "Completed" => "Document signed successfully",
        "Rejected" => "Document was rejected",
        "ChangesRequested" => "Changes requested on your document",
        "Expired" => "Document has expired",
        "ForgotPassword" => "Reset your password",
        _ => $"SignFlow Notification"
    };
}
