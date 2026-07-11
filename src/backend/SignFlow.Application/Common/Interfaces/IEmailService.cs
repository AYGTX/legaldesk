namespace SignFlow.Application.Common.Interfaces;

public record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? From = null,
    string? FromName = null,
    IEnumerable<EmailAttachment>? Attachments = null);

public record EmailAttachment(string FileName, byte[] Content, string ContentType);

public interface IEmailService
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
    Task SendTemplateAsync(string to, string templateName, object model, string? subject = null, CancellationToken cancellationToken = default);
}
