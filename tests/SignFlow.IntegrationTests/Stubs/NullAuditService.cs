using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.IntegrationTests.Stubs;

internal sealed class NullAuditService : IAuditService
{
    public Task LogAsync(Guid documentId, AuditAction action, string ipAddress, string? description = null, Guid? userId = null,
        Guid? recipientId = null, string? userAgent = null, string? location = null, string? actorName = null,
        string? actorEmail = null, string? metadata = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
