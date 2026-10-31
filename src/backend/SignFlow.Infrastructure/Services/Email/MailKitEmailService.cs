using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Infrastructure.Extensions;

namespace SignFlow.Infrastructure.Services.Email;

/// <summary>Talks to the SMTP server. Only the outbox dispatcher calls it; everything else queues through IEmailService.</summary>
public interface ISmtpSender
{
    /// <summary>Sends now; throws when the server cannot take the message, so the dispatcher can retry.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public class MailKitEmailService(IOptions<EmailSettings> settings, ILogger<MailKitEmailService> logger) : ISmtpSender
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

        using (var client = new SmtpClient())
        {
            await client.ConnectAsync(_settings.Host, _settings.Port, _settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);

            if (!string.IsNullOrEmpty(_settings.Username))
                await client.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);

            await client.SendAsync(email, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("Email sent to {To} with subject {Subject}", message.To, message.Subject);
        }
    }

    /// <summary>
    /// Builds the message for a named template. Still a placeholder body (template name + model): real, bilingual
    /// templates are rework spec R-M13-5.
    /// </summary>
    public static EmailMessage RenderTemplate(string to, string templateName, object model, string? subject = null) =>
        new(to, subject ?? GetDefaultSubject(templateName), BuildTemplateBody(templateName, model));

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
        "DocumentReceived" => "A contract is awaiting your action",
        "Reminder" => "Reminder: a contract is still awaiting your action",
        "StepEscalated" => "A contract step is late",
        "Completed" => "Contract fully executed",
        "Rejected" => "Contract was declined",
        "ChangesRequested" => "Revision requested on your contract",
        "Expired" => "Contract validation deadline has passed",
        "ObligationDue" => "Contract obligation deadline reminder",
        "ForgotPassword" => "Reset your password",
        _ => "LegalDesk notification"
    };
}
