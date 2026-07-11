using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class AuditLog : BaseEntity
{
    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public Guid? UserId { get; private set; }
    public ApplicationUser? User { get; private set; }
    public Guid? RecipientId { get; private set; }
    public AuditAction Action { get; private set; }
    public string? Description { get; private set; }
    public string IpAddress { get; private set; } = string.Empty;
    public string? UserAgent { get; private set; }
    public string? Location { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public string? Metadata { get; private set; }
    public DateTime Timestamp { get; private set; } = DateTime.UtcNow;
    public string? ActorName { get; private set; }
    public string? ActorEmail { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(
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
        string? metadata = null)
        => new()
        {
            DocumentId = documentId,
            Action = action,
            IpAddress = ipAddress,
            Description = description,
            UserId = userId,
            RecipientId = recipientId,
            UserAgent = userAgent,
            Location = location,
            ActorName = actorName,
            ActorEmail = actorEmail,
            Metadata = metadata
        };

    public void SetCoordinates(double lat, double lng) { Latitude = lat; Longitude = lng; }
}
