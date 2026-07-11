using SignFlow.Domain.Entities;

namespace SignFlow.Application.Common.Interfaces;

public interface IWorkflowEngine
{
    Task StartAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task AdvanceAsync(Guid documentId, Guid recipientId, CancellationToken cancellationToken = default);
    Task RejectAsync(Guid documentId, Guid recipientId, string reason, CancellationToken cancellationToken = default);
    Task CheckExpirationAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<bool> IsCompleteAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<Recipient?> GetCurrentRecipientAsync(Guid documentId, CancellationToken cancellationToken = default);
}
