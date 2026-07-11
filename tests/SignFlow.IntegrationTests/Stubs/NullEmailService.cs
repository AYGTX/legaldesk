using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

internal sealed class NullEmailService : IEmailService
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendTemplateAsync(string to, string templateName, object model, string? subject = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
