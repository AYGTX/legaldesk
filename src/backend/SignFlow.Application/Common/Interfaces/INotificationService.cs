using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Interfaces;

/// <summary>Creates persistent in-app notifications and pushes them to the user in real time.</summary>
public interface INotificationService
{
    Task NotifyUserAsync(
        Guid userId,
        NotificationType type,
        string subject,
        string body,
        Guid? documentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Notifies a recipient by email if they are a registered user; otherwise no-ops.</summary>
    Task NotifyByEmailAsync(
        string email,
        NotificationType type,
        string subject,
        string body,
        Guid? documentId = null,
        CancellationToken cancellationToken = default);
}
