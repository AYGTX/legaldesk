using System.Text.Json;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Services.Email;

/// <summary>
/// The IEmailService every feature uses: it queues the message in the outbox and returns (R-M4-9). Sending,
/// retries and failures belong to <see cref="OutboxDispatcherJob"/>, never to the user's action.
/// </summary>
public class OutboxEmailService(IApplicationDbContext db) : IEmailService
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        db.OutboxMessages.Add(OutboxMessage.Create(
            OutboxMessage.EmailKind, JsonSerializer.Serialize(message, Json), DateTime.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SendTemplateAsync(string to, string templateName, object model, string? subject = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(MailKitEmailService.RenderTemplate(to, templateName, model, subject), cancellationToken);
}
