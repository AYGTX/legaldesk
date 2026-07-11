namespace SignFlow.Application.Common.Interfaces;

public interface INotificationHub
{
    Task SendToUserAsync(string userId, string eventName, object data, CancellationToken cancellationToken = default);
    Task SendToGroupAsync(string groupName, string eventName, object data, CancellationToken cancellationToken = default);
    Task BroadcastAsync(string eventName, object data, CancellationToken cancellationToken = default);
}
