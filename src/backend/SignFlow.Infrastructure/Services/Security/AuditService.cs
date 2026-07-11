using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Services.Security;

public class AuditService(IApplicationDbContext db) : IAuditService
{
    public async Task LogAsync(
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
        CancellationToken cancellationToken = default)
    {
        var log = AuditLog.Create(documentId, action, ipAddress, description, userId, recipientId, userAgent, location, actorName, actorEmail, metadata);
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);
    }
}
