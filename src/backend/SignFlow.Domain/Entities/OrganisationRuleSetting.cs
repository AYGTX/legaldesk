using System.ComponentModel.DataAnnotations;

namespace SignFlow.Domain.Entities;

/// <summary>
/// One organisation's choice about one rule of Harvey's grid (R-M0-7): switched off, or reviewed at another severity.
/// Harvey stays tenant-free (the law is public); these settings travel with each review request.
/// </summary>
public class OrganisationRuleSetting : TenantEntity
{
    [MaxLength(80)]
    public string RuleKey { get; private set; } = string.Empty;

    public bool Enabled { get; private set; } = true;

    /// <summary>"Critical", "High", "Medium" or "Low"; null keeps the rule's own severity.</summary>
    [MaxLength(32)]
    public string? SeverityOverride { get; private set; }

    public Guid? ChangedByUserId { get; private set; }
    public DateTime ChangedAt { get; private set; }

    private OrganisationRuleSetting() { }

    public static OrganisationRuleSetting For(string ruleKey) => new() { RuleKey = ruleKey };

    public void Change(bool enabled, string? severityOverride, Guid userId)
    {
        Enabled = enabled;
        SeverityOverride = severityOverride;
        ChangedByUserId = userId;
        ChangedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>A setting that changes nothing can be deleted.</summary>
    public bool IsDefault => Enabled && SeverityOverride is null;
}
