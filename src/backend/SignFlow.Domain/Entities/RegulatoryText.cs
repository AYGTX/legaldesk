using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// A tracked regulatory text (law, article, sectoral rule) the legal team curates and publishes.
/// Published texts feed the compliance-analysis keyword scan and trigger regulatory alerts on
/// contracts already in force when they are published.
/// </summary>
public class RegulatoryText : BaseEntity
{
    public string Title { get; private set; } = string.Empty;

    /// <summary>Article/law reference, e.g. "Loi organique 2004-63, art. 12".</summary>
    public string? Reference { get; private set; }

    /// <summary>Plain-language summary of the text, as an HTML fragment.</summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>Comma-separated keywords used by the compliance scan to match this text against a contract.</summary>
    public string Keywords { get; private set; } = string.Empty;

    /// <summary>ISO language code of the summary ("en" or "fr").</summary>
    public string Language { get; private set; } = "en";

    public RegulatoryJurisdiction Jurisdiction { get; private set; }
    public RegulatoryCategory Category { get; private set; }
    public RegulatoryTextStatus Status { get; private set; } = RegulatoryTextStatus.Draft;

    public DateTime? EffectiveDate { get; private set; }

    /// <summary>Optional scope: when set, the text only applies to this contract family.</summary>
    public Guid? RequestTypeId { get; private set; }
    public RequestType? RequestType { get; private set; }

    /// <summary>Where the official text can be read: the gazette issue or the legislation portal page.</summary>
    public string? SourceUrl { get; private set; }

    /// <summary>
    /// A person checked the stored articles against the official source and signed off. Only a text with this set
    /// may be presented as law; everything else is a note someone typed. Any change to what was checked — the
    /// articles, the reference or the source — clears it.
    /// </summary>
    public Guid? VerifiedById { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public bool IsVerified => VerifiedAt.HasValue;

    /// <summary>The law word for word, article by article. Empty for a text that only has a summary.</summary>
    public ICollection<RegulatoryArticle> Articles { get; private set; } = [];

    public Guid CreatedById { get; private set; }
    public ApplicationUser CreatedByUser { get; private set; } = null!;

    private RegulatoryText() { }

    public static RegulatoryText Create(
        string title,
        RegulatoryCategory category,
        RegulatoryJurisdiction jurisdiction,
        string summary,
        string keywords,
        Guid createdById,
        string language = "en",
        string? reference = null,
        DateTime? effectiveDate = null,
        Guid? requestTypeId = null,
        string? sourceUrl = null)
        => new()
        {
            SourceUrl = sourceUrl,
            Title = title,
            Category = category,
            Jurisdiction = jurisdiction,
            Summary = summary,
            Keywords = keywords,
            CreatedById = createdById,
            Language = language,
            Reference = reference,
            EffectiveDate = effectiveDate,
            RequestTypeId = requestTypeId,
            Status = RegulatoryTextStatus.Draft
        };

    public void Update(
        string title,
        RegulatoryCategory category,
        RegulatoryJurisdiction jurisdiction,
        string summary,
        string keywords,
        string language,
        string? reference,
        DateTime? effectiveDate,
        Guid? requestTypeId,
        string? sourceUrl = null)
    {
        // What a verifier attested to is "these articles are the text published at this source". Changing the reference
        // or the source changes that claim, so the sign-off no longer stands.
        if (!string.Equals(Reference, reference, StringComparison.Ordinal)
            || !string.Equals(SourceUrl, sourceUrl, StringComparison.Ordinal))
            ClearVerification();

        SourceUrl = sourceUrl;
        Title = title;
        Category = category;
        Jurisdiction = jurisdiction;
        Summary = summary;
        Keywords = keywords;
        Language = language;
        Reference = reference;
        EffectiveDate = effectiveDate;
        RequestTypeId = requestTypeId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkVerified(Guid userId)
    {
        VerifiedById = userId;
        VerifiedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ClearVerification()
    {
        VerifiedById = null;
        VerifiedAt = null;
    }

    public void SetStatus(RegulatoryTextStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }
}
