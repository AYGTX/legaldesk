using System.ComponentModel.DataAnnotations;
using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// A client organisation (rework spec M0). Every business row belongs to exactly one; no user of one organisation
/// can read another's data.
/// </summary>
public class Organisation : BaseEntity
{
    /// <summary>The organisation every row created before multi-tenancy was moved into (migration AddOrganisations).</summary>
    public static readonly Guid DefaultId = new("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11");

    [MaxLength(200)]
    public string Name { get; private set; } = string.Empty;

    /// <summary>Unique, URL-safe: lowercase letters, digits and hyphens.</summary>
    [MaxLength(80)]
    public string Slug { get; private set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2.</summary>
    [MaxLength(2)]
    public string Country { get; private set; } = "TN";

    /// <summary>fr or en.</summary>
    [MaxLength(5)]
    public string DefaultLanguage { get; private set; } = "fr";

    /// <summary>Off by default (D3): contract text leaves the server only when an administrator turns this on.</summary>
    public ModelMode ModelMode { get; private set; } = ModelMode.Off;

    public int RetentionYears { get; private set; } = 10;

    /// <summary>IANA time zone of the dates the server writes on contracts (signing dates, R-M5-7).</summary>
    [MaxLength(64)]
    public string TimeZoneId { get; private set; } = "Africa/Tunis";

    /// <summary>.NET date format of those dates.</summary>
    [MaxLength(32)]
    public string DateFormat { get; private set; } = "dd/MM/yyyy";

    public bool IsActive { get; private set; } = true;

    /// <summary>Days before a contract's notice deadline (else end date) its alerts go out (R-M10-3), most distant first.</summary>
    [MaxLength(64)]
    public string AlertOffsets { get; private set; } = "60,30,14,7,0";

    /// <summary>Local date the lifecycle sweep last ran for this organisation, so it runs once a day at 08:00 local (R-M10-8).</summary>
    public DateTime? LastLifecycleSweepOn { get; private set; }

    public void SetAlertOffsets(string offsets) => AlertOffsets = offsets;

    public void MarkLifecycleSweep(DateTime localDate) => LastLifecycleSweepOn = localDate.Date;

    private Organisation() { }

    public static Organisation Create(string name, string slug, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Slug = slug
    };
}
