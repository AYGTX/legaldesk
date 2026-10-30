using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.IntegrationTests.Stubs;

/// <summary>Writes nothing, but keeps what was logged so a scenario can check the trail.</summary>
internal sealed class NullAuditService : IAuditService
{
    public List<(Guid DocumentId, AuditAction Action, string? Description)> Entries { get; } = [];

    public Task LogAsync(Guid documentId, AuditAction action, string ipAddress, string? description = null, Guid? userId = null,
        Guid? recipientId = null, string? userAgent = null, string? location = null, string? actorName = null,
        string? actorEmail = null, string? metadata = null, CancellationToken cancellationToken = default)
    {
        lock (Entries) Entries.Add((documentId, action, description));
        return Task.CompletedTask;
    }
}
