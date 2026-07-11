using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Interfaces;

public interface IAuditService
{
    Task LogAsync(
        Guid documentId,
        AuditAction action,
        string ipAddress,
        string? description = null,
        Guid? userId = null,
        Guid? recipientId = null,
        string? userAgent = null,
        string? location = null,
        string? actorName = null,
        string? actorEmail = null,
        string? metadata = null,
        CancellationToken cancellationToken = default);
}
