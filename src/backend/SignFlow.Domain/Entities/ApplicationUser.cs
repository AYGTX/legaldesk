using Microsoft.AspNetCore.Identity;

namespace SignFlow.Domain.Entities;

public class ApplicationUser : IdentityUser<Guid>, ITenantOwned
{
    /// <summary>The one organisation this user belongs to (R-M0-2).</summary>
    public Guid OrganisationId { get; private set; }

    public void AssignOrganisation(Guid organisationId)
    {
        if (OrganisationId != Guid.Empty && OrganisationId != organisationId)
            throw new InvalidOperationException("A user cannot move to another organisation.");
        OrganisationId = organisationId;
    }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string? Department { get; set; }
    public string? Title { get; set; }
    public string? AvatarUrl { get; set; }
    public string? SignatureUrl { get; set; }
    public string? InitialsUrl { get; set; }
    public bool MfaEnabled { get; set; }
    public string? MfaSecret { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiry { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public ICollection<Document> Documents { get; set; } = [];
    public ICollection<AuditLog> AuditLogs { get; set; } = [];
    public ICollection<Comment> Comments { get; set; } = [];
}
