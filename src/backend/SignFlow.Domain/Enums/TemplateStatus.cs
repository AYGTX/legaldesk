namespace SignFlow.Domain.Enums;

/// <summary>Where a template version is in its life (R-M6-5). Only Approved versions are offered to requesters.</summary>
public enum TemplateStatus
{
    Draft = 0,
    InLegalReview = 1,
    Approved = 2,
    Retired = 3
}
