using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Infrastructure.Services.SignalR;

/// <summary>
/// Live pushes are best effort: the change they announce is already saved, and the page reloads it on its next
/// visit, so a push that fails is logged and never fails the action that caused it (R-M4-9).
/// </summary>
public class NotificationHubService(IHubContext<SignFlowHub> hubContext, ILogger<NotificationHubService> logger) : INotificationHub
{
    public Task SendToUserAsync(string userId, string eventName, object data, CancellationToken cancellationToken = default)
        => TrySend(() => hubContext.Clients.Group($"user-{userId}").SendAsync(eventName, data, cancellationToken), eventName);

    public Task SendToGroupAsync(string groupName, string eventName, object data, CancellationToken cancellationToken = default)
        => TrySend(() => hubContext.Clients.Group(groupName).SendAsync(eventName, data, cancellationToken), eventName);

    public Task BroadcastAsync(string eventName, object data, CancellationToken cancellationToken = default)
        => TrySend(() => hubContext.Clients.All.SendAsync(eventName, data, cancellationToken), eventName);

    private async Task TrySend(Func<Task> send, string eventName)
    {
        try
        {
            await send();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Live push {EventName} could not be delivered", eventName);
        }
    }
}
