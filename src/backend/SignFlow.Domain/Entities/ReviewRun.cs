using System.ComponentModel.DataAnnotations;
using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// One Harvey review of one version of a contract (R-M8-9). It runs in the background: queued by the request, then
/// completed with the grid version it used, or failed with an error code. A failed run changes no finding.
/// </summary>
public class ReviewRun : TenantEntity
{
    public const string Unavailable = "review.unavailable";
    public const string TimedOut = "review.timeout";
    public const string NoText = "review.no_text";
    public const string Stale = "review.stale";

    public Guid DocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public ReviewRunStatus Status { get; private set; } = ReviewRunStatus.Queued;
    public Guid? RequestedByUserId { get; private set; }

    /// <summary>The date the law and rules are read at: the signature date when known, else the day of the review.</summary>
    public DateTime AsOfDate { get; private set; }

    public DateTime? StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }

    /// <summary>Harvey's fingerprint of the rule versions used: same text, grid version and settings give the same findings.</summary>
    [MaxLength(64)]
    public string? GridVersion { get; private set; }

    public int FindingCount { get; private set; }

    [MaxLength(64)]
    public string? ErrorCode { get; private set; }

    [MaxLength(1000)]
    public string? Error { get; private set; }

    private ReviewRun() { }

    public static ReviewRun Queue(Guid documentId, int versionNumber, Guid? requestedBy, DateTime asOfDate) => new()
    {
        DocumentId = documentId,
        VersionNumber = versionNumber,
        RequestedByUserId = requestedBy,
        AsOfDate = asOfDate.Date,
        Status = ReviewRunStatus.Queued
    };

    public bool IsFinished => Status is ReviewRunStatus.Completed or ReviewRunStatus.Failed;

    public void Start()
    {
        Status = ReviewRunStatus.Running;
        StartedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Complete(string? gridVersion, int findingCount)
    {
        Status = ReviewRunStatus.Completed;
        GridVersion = gridVersion;
        FindingCount = findingCount;
        FinishedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Fail(string code, string message)
    {
        Status = ReviewRunStatus.Failed;
        ErrorCode = code;
        Error = message.Length > 1000 ? message[..1000] : message;
        FinishedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
