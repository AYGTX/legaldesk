namespace SignFlow.Domain.Entities;

/// <summary>
/// Central configuration for a category of signing request (e.g. Vacation Request, Purchase Approval).
/// Defines the SLA (signing deadline) and presentation used when users create requests of this type.
/// </summary>
public class RequestType : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Maximum time (in hours) allowed to complete the signature workflow.</summary>
    public int SlaHours { get; private set; }

    public bool IsActive { get; private set; } = true;
    public string? Icon { get; private set; }
    public string? Color { get; private set; }

    private RequestType() { }

    public static RequestType Create(string name, string? description, int slaHours, string? icon, string? color)
        => new()
        {
            Name = name,
            Description = description,
            SlaHours = slaHours,
            Icon = icon,
            Color = color,
            IsActive = true
        };

    public void Update(string name, string? description, int slaHours, string? icon, string? color)
    {
        Name = name;
        Description = description;
        SlaHours = slaHours;
        Icon = icon;
        Color = color;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>The workflow a contract of this family follows when its template names none (R-M6-8).</summary>
    public Guid? DefaultPresetId { get; private set; }

    /// <summary>
    /// The Harvey rule sets that apply to the family (R-M6-8), comma-separated keys from <see cref="RuleSets"/>. Recorded
    /// now; Harvey does not group its rules into sets yet, so every approved rule still applies to every contract.
    /// </summary>
    public string? RuleSetKeys { get; private set; }

    /// <summary>The rule sets a family can name.</summary>
    public static readonly string[] RuleSets = ["banking", "competition", "consumer", "data-protection", "employment", "public-procurement"];

    public IReadOnlyList<string> RuleSetList =>
        string.IsNullOrWhiteSpace(RuleSetKeys) ? [] : RuleSetKeys.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public void SetFamily(Guid? defaultPresetId, IEnumerable<string>? ruleSets)
    {
        DefaultPresetId = defaultPresetId;
        var keys = (ruleSets ?? []).Select(k => k.Trim().ToLowerInvariant()).Where(RuleSets.Contains).Distinct().ToList();
        RuleSetKeys = keys.Count == 0 ? null : string.Join(',', keys);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }
}
