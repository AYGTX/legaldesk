using System.ComponentModel.DataAnnotations;
using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// One adjustment the pre-flight review says the contract needs before it goes into a validation
/// circuit. Items are reconciled across re-runs by <see cref="SourceKey"/>, so an owner who has
/// already resolved or dismissed a finding does not see it come back after the next analysis.
/// </summary>
public class DocumentReadinessItem : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;

    public ReadinessItemCategory Category { get; private set; }

    /// <summary>"Critical", "High", "Medium" or "Low" — mirrors RiskFinding.Severity.</summary>
    [MaxLength(32)]
    public string Severity { get; private set; } = "Medium";

    /// <summary>
    /// Stable identity of the underlying finding ("risk:{clauseType}", "compliance:{regulationId}",
    /// "obligation:missing"). This is what makes a re-run an update rather than a duplicate.
    /// </summary>
    [MaxLength(256)]
    public string SourceKey { get; private set; } = string.Empty;

    [MaxLength(300)]
    public string Title { get; private set; } = string.Empty;

    public string Detail { get; private set; } = string.Empty;

    /// <summary>Why this matters legally — carried through from the model review when it said.</summary>
    public string? WhyItMatters { get; private set; }

    /// <summary>The concrete adjustment to make.</summary>
    public string? RecommendedAction { get; private set; }

    /// <summary>Suggested contract wording, when the review proposed one.</summary>
    public string? ProposedLanguage { get; private set; }

    public ReadinessItemStatus Status { get; private set; } = ReadinessItemStatus.Open;
    public string? ResolutionNote { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    private DocumentReadinessItem() { }

    public static DocumentReadinessItem Create(
        Guid documentId,
        ReadinessItemCategory category,
        string severity,
        string sourceKey,
        string title,
        string detail,
        string? whyItMatters = null,
        string? recommendedAction = null,
        string? proposedLanguage = null)
        => new()
        {
            DocumentId = documentId,
            Category = category,
            Severity = severity,
            SourceKey = sourceKey,
            Title = title,
            Detail = detail,
            WhyItMatters = whyItMatters,
            RecommendedAction = recommendedAction,
            ProposedLanguage = proposedLanguage,
            Status = ReadinessItemStatus.Open
        };

    /// <summary>
    /// Refreshes the wording of an item a re-run found again. The resolution state is deliberately
    /// left alone — the owner's judgement outlives one analysis pass.
    /// </summary>
    public void RefreshContent(
        ReadinessItemCategory category,
        string severity,
        string title,
        string detail,
        string? whyItMatters,
        string? recommendedAction,
        string? proposedLanguage)
    {
        Category = category;
        Severity = severity;
        Title = title;
        Detail = detail;
        WhyItMatters = whyItMatters;
        RecommendedAction = recommendedAction;
        ProposedLanguage = proposedLanguage;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetStatus(ReadinessItemStatus status, Guid? userId, string? note = null)
    {
        Status = status;
        ResolutionNote = note;
        if (status == ReadinessItemStatus.Open)
        {
            ResolvedByUserId = null;
            ResolvedAt = null;
        }
        else
        {
            ResolvedByUserId = userId;
            ResolvedAt = DateTime.UtcNow;
        }
        UpdatedAt = DateTime.UtcNow;
    }
}
