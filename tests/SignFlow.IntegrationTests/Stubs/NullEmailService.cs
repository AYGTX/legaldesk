using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

/// <summary>Sends nothing, but keeps what would have been sent, so a scenario can follow a link or read a code.</summary>
internal sealed class NullEmailService : IEmailService
{
    public List<(string To, string Template, object Model)> Sent { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendTemplateAsync(string to, string templateName, object model, string? subject = null, CancellationToken cancellationToken = default)
    {
        lock (Sent) Sent.Add((to, templateName, model));
        return Task.CompletedTask;
    }

    /// <summary>A value of the last e-mail of this template to this address (the model is an anonymous object).</summary>
    public string Last(string to, string template, string property)
    {
        var model = Sent.Last(s => s.To == to && s.Template == template).Model;
        return model.GetType().GetProperty(property)!.GetValue(model)!.ToString()!;
    }
}
