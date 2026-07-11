using Microsoft.AspNetCore.SignalR;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Infrastructure.Services.SignalR;

public class NotificationHubService(IHubContext<SignFlowHub> hubContext) : INotificationHub
{
    public async Task SendToUserAsync(string userId, string eventName, object data, CancellationToken cancellationToken = default)
        => await hubContext.Clients.Group($"user-{userId}").SendAsync(eventName, data, cancellationToken);

    public async Task SendToGroupAsync(string groupName, string eventName, object data, CancellationToken cancellationToken = default)
        => await hubContext.Clients.Group(groupName).SendAsync(eventName, data, cancellationToken);

    public async Task BroadcastAsync(string eventName, object data, CancellationToken cancellationToken = default)
        => await hubContext.Clients.All.SendAsync(eventName, data, cancellationToken);
}
