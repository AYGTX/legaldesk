using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// A reusable, legally-vetted contract clause held in the shared clause library.
/// Drafters insert approved clauses into contract templates instead of re-writing standard
/// language, so the organisation's negotiated positions stay consistent across contracts.
/// </summary>
public class Clause : TenantEntity
{
    public string Title { get; private set; } = string.Empty;
    public ClauseCategory Category { get; private set; }

    /// <summary>The clause text itself, as an HTML fragment inserted into the contract body.</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>Internal legal note: when to use this clause, negotiation stance, fallback positions.</summary>
    public string? Guidance { get; private set; }

    /// <summary>ISO language code of the clause text ("en" or "fr") — contracts are drafted in both.</summary>
    public string Language { get; private set; } = "en";

    public ClauseStatus Status { get; private set; } = ClauseStatus.Draft;

    /// <summary>Legal exposure this clause carries if accepted as drafted: "Low", "Medium" or "High".</summary>
    public string? RiskLevel { get; private set; }

    /// <summary>Incremented whenever the clause text changes, so contracts can cite the wording they used.</summary>
    public int Version { get; private set; } = 1;

    /// <summary>How many times this clause has been inserted into a contract template.</summary>
    public int UsageCount { get; private set; }

    /// <summary>Optional scope: when set, the clause is only suggested for this contract family.</summary>
    public Guid? RequestTypeId { get; private set; }
    public RequestType? RequestType { get; private set; }

    public Guid CreatedById { get; private set; }
    public ApplicationUser CreatedByUser { get; private set; } = null!;

    private Clause() { }

    public static Clause Create(
        string title,
        ClauseCategory category,
        string body,
        Guid createdById,
        string language = "en",
        string? guidance = null,
        string? riskLevel = null,
        Guid? requestTypeId = null)
        => new()
        {
            Title = title,
            Category = category,
            Body = body,
            CreatedById = createdById,
            Language = language,
            Guidance = guidance,
            RiskLevel = riskLevel,
            RequestTypeId = requestTypeId,
            Status = ClauseStatus.Draft
        };

    /// <summary>
    /// Updates the clause. The version is bumped only when the wording actually changes — editing
    /// the title or the internal guidance leaves the cited version intact.
    /// </summary>
    public void Update(
        string title,
        ClauseCategory category,
        string body,
        string language,
        string? guidance,
        string? riskLevel,
        Guid? requestTypeId)
    {
        if (!string.Equals(Body, body, StringComparison.Ordinal))
            Version++;

        Title = title;
        Category = category;
        Body = body;
        Language = language;
        Guidance = guidance;
        RiskLevel = riskLevel;
        RequestTypeId = requestTypeId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetStatus(ClauseStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecordUsage()
    {
        UsageCount++;
        UpdatedAt = DateTime.UtcNow;
    }
}
