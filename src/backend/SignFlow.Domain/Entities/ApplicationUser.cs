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
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Preferred language for e-mails and notifications (fr or en).</summary>
    public string PreferredLanguage { get; set; } = "fr";

    // Two-factor sign-in uses Identity's TwoFactorEnabled and authenticator key; sessions live in UserSession.

    public string FullName => $"{FirstName} {LastName}".Trim();

    public ICollection<Document> Documents { get; set; } = [];
    public ICollection<AuditLog> AuditLogs { get; set; } = [];
    public ICollection<Comment> Comments { get; set; } = [];
}
