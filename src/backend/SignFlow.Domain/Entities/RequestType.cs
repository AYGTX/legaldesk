namespace SignFlow.Domain.Entities;

/// <summary>
/// Central configuration for a category of signing request (e.g. Vacation Request, Purchase Approval).
/// Defines the SLA (signing deadline) and presentation used when users create requests of this type.
/// </summary>
public class RequestType : BaseEntity
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

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }
}
