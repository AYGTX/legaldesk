using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// What HTML and PDF templates share about their life (R-M6-5): Draft → In legal review → Approved → Retired. Only an
/// approved version is offered to requesters, and an approved version is never edited: a change is made on its next
/// version, a draft copy, and the approved one stays in use until that draft is approved in its turn.
/// </summary>
public abstract class VersionedTemplate : TenantEntity
{
    public TemplateStatus Status { get; protected set; } = TemplateStatus.Draft;

    /// <summary>1 for the first version; each next version adds one. Contracts record the version they came from.</summary>
    public int Version { get; protected set; } = 1;

    /// <summary>The version this one was drafted from.</summary>
    public Guid? ParentTemplateId { get; protected set; }

    public Guid? SubmittedById { get; protected set; }
    public DateTime? SubmittedAt { get; protected set; }
    public Guid? ApprovedById { get; protected set; }
    public DateTime? ApprovedAt { get; protected set; }

    public bool IsEditable => Status is TemplateStatus.Draft or TemplateStatus.InLegalReview;

    /// <summary>An edit sends a template under review back to its author: the review was of the earlier wording.</summary>
    protected void Edited()
    {
        if (!IsEditable) throw new InvalidOperationException("An approved or retired template is not edited: draft its next version.");
        if (Status == TemplateStatus.InLegalReview)
        {
            Status = TemplateStatus.Draft;
            SubmittedById = null;
            SubmittedAt = null;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void SubmitForReview(Guid by)
    {
        if (Status != TemplateStatus.Draft) throw new InvalidOperationException("Only a draft is sent to legal review.");
        Status = TemplateStatus.InLegalReview;
        SubmittedById = by;
        SubmittedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Approve(Guid by)
    {
        if (Status != TemplateStatus.InLegalReview) throw new InvalidOperationException("Only a template in legal review is approved.");
        Status = TemplateStatus.Approved;
        ApprovedById = by;
        ApprovedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Retire()
    {
        Status = TemplateStatus.Retired;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Templates in use before versions existed, and seeded ones: approved as they are.</summary>
    public void ImportApproved()
    {
        Status = TemplateStatus.Approved;
        ApprovedAt ??= DateTime.UtcNow;
    }

    protected void BecomeNextVersionOf(VersionedTemplate previous)
    {
        Version = previous.Version + 1;
        ParentTemplateId = previous.Id;
        Status = TemplateStatus.Draft;
    }
}
