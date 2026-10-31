using SignFlow.Domain.Entities;

namespace SignFlow.Application.Common.Interfaces;

public interface IWorkflowEngine
{
    /// <summary>Opens the contract's current round (a new one after a revision) and its first stage.</summary>
    Task StartAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task AdvanceAsync(Guid documentId, Guid recipientId, CancellationToken cancellationToken = default);
    Task RejectAsync(Guid documentId, Guid recipientId, string reason, CancellationToken cancellationToken = default);
    Task RequestChangesAsync(Guid documentId, Guid recipientId, string reason, CancellationToken cancellationToken = default);
    Task CheckExpirationAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<bool> IsCompleteAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<Recipient?> GetCurrentRecipientAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Hands the participant's open step to someone else (R-M4-8). Returns the participant who now acts.</summary>
    Task<Recipient> DelegateAsync(Guid documentId, Guid recipientId, string name, string email, string note, CancellationToken cancellationToken = default);

    /// <summary>Whether the participant has an open step in the current round: it is their turn.</summary>
    Task<bool> IsTurnAsync(Guid recipientId, CancellationToken cancellationToken = default);

    /// <summary>Sends the reminders and escalations that are due in the organisation in scope (R-M4-6).</summary>
    Task<int> FollowUpAsync(DateTime utcNow, CancellationToken cancellationToken = default);
}
