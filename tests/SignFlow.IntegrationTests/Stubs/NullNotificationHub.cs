using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

internal sealed class NullNotificationHub : INotificationHub
{
    public Task SendToUserAsync(string userId, string eventName, object data, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendToGroupAsync(string groupName, string eventName, object data, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task BroadcastAsync(string eventName, object data, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
