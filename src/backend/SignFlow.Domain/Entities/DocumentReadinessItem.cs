using System.ComponentModel.DataAnnotations;
using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// One risk Harvey found in a contract: one item per (rule, clause), whatever the number of hits in that clause
/// (R-M8-3). An item lives across versions of the contract: a review of a newer version that no longer finds it marks
/// it <see cref="ReadinessItemStatus.Fixed"/>; a review of the same version never changes its state (R-M8-4).
/// </summary>
public class DocumentReadinessItem : TenantEntity
{
    public const int MaxReasonLength = 1000;

    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;

    /// <summary>Always <see cref="ReadinessItemCategory.Risk"/> for items from Harvey; other values are history.</summary>
    public ReadinessItemCategory Category { get; private set; }

    /// <summary>"Critical", "High", "Medium" or "Low", as the rule version (and the organisation's setting) says.</summary>
    [MaxLength(32)]
    public string Severity { get; private set; } = "Medium";

    /// <summary>"{ruleKey}|{clauseReference}": what makes a re-run an update rather than a duplicate.</summary>
    [MaxLength(256)]
    public string SourceKey { get; private set; } = string.Empty;

    /// <summary>Harvey's rule key; null only on items from before the review rework.</summary>
    [MaxLength(80)]
    public string? RuleKey { get; private set; }

    public int? RuleVersion { get; private set; }

    /// <summary>The lawyer who approved that rule version, and when (R-M8-11).</summary>
    [MaxLength(300)]
    public string? RuleApprovedBy { get; private set; }

    public DateTime? RuleApprovedAt { get; private set; }

    /// <summary>The clause as the review names it ("Article 5 — Responsabilité").</summary>
    [MaxLength(300)]
    public string? ClauseReference { get; private set; }

    /// <summary>JSON array of the contract's exact words that raised the finding, one per hit.</summary>
    public string? QuotesJson { get; private set; }

    /// <summary>CodeCheck, MeaningMatch or Phrase.</summary>
    [MaxLength(32)]
    public string? DetectionKind { get; private set; }

    /// <summary>Similarity of a meaning match (0–1); null for a check in code or a phrase.</summary>
    public double? Confidence { get; private set; }

    /// <summary>The rule's label.</summary>
    [MaxLength(300)]
    public string Title { get; private set; } = string.Empty;

    /// <summary>The rule's explanation of the finding.</summary>
    public string Detail { get; private set; } = string.Empty;

    /// <summary>Items from before the review rework: the law as formatted text. New items use <see cref="ArticlesJson"/>.</summary>
    public string? WhyItMatters { get; private set; }

    /// <summary>JSON array of the verified articles the rule links (citation, quote, why, approved explanation).</summary>
    public string? ArticlesJson { get; private set; }

    public string? RecommendedAction { get; private set; }

    /// <summary>The lawyer-approved model clause for the rule, in the contract's language.</summary>
    public string? ProposedLanguage { get; private set; }

    public ReadinessItemStatus Status { get; private set; } = ReadinessItemStatus.Open;

    /// <summary>Why the risk was accepted or judged not applicable; required for both (R-M8-4).</summary>
    public string? Reason { get; private set; }

    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAt { get; private set; }

    public int? FirstFoundInVersion { get; private set; }

    /// <summary>The contract version whose review no longer found it.</summary>
    public int? FixedInVersion { get; private set; }

    /// <summary>The last completed review run that reported this item. The document's current findings are the items
    /// whose run is the document's <see cref="Document.LastReviewRunId"/>.</summary>
    public Guid? LastSeenRunId { get; private set; }

    public int? LastSeenVersion { get; private set; }

    private DocumentReadinessItem() { }

    /// <summary>An item as items were before the review rework (kept for tests of old data and the history).</summary>
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

    public static string KeyOf(string ruleKey, string? clauseReference) => $"{ruleKey}|{clauseReference}";

    /// <summary>A risk Harvey found for the first time in this contract.</summary>
    public static DocumentReadinessItem Found(Guid documentId, ReviewFinding finding, Guid runId, int version)
    {
        var item = new DocumentReadinessItem
        {
            DocumentId = documentId,
            Category = ReadinessItemCategory.Risk,
            SourceKey = KeyOf(finding.RuleKey, finding.ClauseReference),
            RuleKey = finding.RuleKey,
            ClauseReference = finding.ClauseReference,
            FirstFoundInVersion = version,
            Status = ReadinessItemStatus.Open
        };
        item.Seen(finding, runId, version);
        return item;
    }

    /// <summary>
    /// A review found the item (again). The content follows the rule version that found it. A Fixed item that comes
    /// back in a newer version is open again; any other state is the owner's decision and stays.
    /// </summary>
    public void Seen(ReviewFinding finding, Guid runId, int version)
    {
        Severity = finding.Severity;
        Title = finding.Label;
        Detail = finding.Explanation;
        RecommendedAction = finding.RecommendedAction;
        ProposedLanguage = finding.ProposedLanguage;
        ArticlesJson = finding.ArticlesJson;
        RuleVersion = finding.RuleVersion;
        RuleApprovedBy = finding.RuleApprovedBy;
        RuleApprovedAt = finding.RuleApprovedAt;
        QuotesJson = finding.QuotesJson;
        DetectionKind = finding.DetectionKind;
        Confidence = finding.Confidence;

        if (Status == ReadinessItemStatus.Fixed)
        {
            Status = ReadinessItemStatus.Open;
            FixedInVersion = null;
            Reason = null;
            DecidedByUserId = null;
            DecidedAt = null;
        }

        LastSeenRunId = runId;
        LastSeenVersion = version;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>A review of a newer version no longer finds the risk. Only the system sets this state.</summary>
    public void MarkFixed(int version)
    {
        Status = ReadinessItemStatus.Fixed;
        FixedInVersion = version;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>The owner's (or a manager's) decision: Accepted, NotApplicable, or back to Open. Who may decide what is
    /// checked by the caller; a reason is required for both decisions.</summary>
    public void Decide(ReadinessItemStatus status, Guid userId, string? reason)
    {
        if (status == ReadinessItemStatus.Fixed)
            throw new InvalidOperationException("Only a review of a newer version marks a finding fixed.");

        if (status == ReadinessItemStatus.Open)
        {
            Reason = null;
            DecidedByUserId = null;
            DecidedAt = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new InvalidOperationException("A decision needs a reason.");
            Reason = reason.Trim();
            DecidedByUserId = userId;
            DecidedAt = DateTime.UtcNow;
        }

        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// One (rule, clause) finding of a review, with everything the item shows. Quotes and articles are already JSON so the
/// domain stays free of serialisation choices.
/// </summary>
public record ReviewFinding(
    string RuleKey,
    string? ClauseReference,
    string Severity,
    string Label,
    string Explanation,
    string? RecommendedAction,
    string? ProposedLanguage,
    string? ArticlesJson,
    int? RuleVersion,
    string? RuleApprovedBy,
    DateTime? RuleApprovedAt,
    string? QuotesJson,
    string? DetectionKind,
    double? Confidence);
