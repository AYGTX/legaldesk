using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Services;

/// <summary>The grade of a contract's current findings (R-M8-5 to R-M8-7).</summary>
public record ReviewGrade(
    ReadinessVerdict Verdict,
    int ReadinessScore,
    int RiskScore,
    int OpenCount,
    int CriticalCount,
    int HighCount,
    int HandledCount);

/// <summary>
/// Grades Harvey's findings with fixed arithmetic, never a model, so the verdict is reproducible and explainable line by
/// line. Readiness counts the findings still open; the risk score counts every current finding, handled or not, so it
/// changes only when the text does.
/// </summary>
public static class ReviewGrading
{
    public const string Critical = "Critical";
    public const string High = "High";
    public const string Medium = "Medium";
    public const string Low = "Low";

    public static readonly IReadOnlyList<string> Severities = [Critical, High, Medium, Low];

    public static bool IsSeverity(string? value) => value is not null && Severities.Contains(value);

    /// <summary>0 for Critical up to 3 for Low (and anything unknown).</summary>
    public static int Rank(string? severity) => severity switch
    {
        Critical => 0,
        High => 1,
        Medium => 2,
        _ => 3
    };

    /// <summary>Points one finding costs (D5): 25, 10, 3, 1.</summary>
    public static int Weight(string? severity) => severity switch
    {
        Critical => 25,
        High => 10,
        Medium => 3,
        _ => 1
    };

    public static ReviewGrade Grade(IReadOnlyCollection<DocumentReadinessItem> current)
    {
        var open = current.Where(i => i.Status == ReadinessItemStatus.Open).ToList();
        var critical = open.Count(i => i.Severity == Critical);
        var high = open.Count(i => i.Severity == High);

        var verdict = critical > 0 ? ReadinessVerdict.Blocked
            : high > 0 ? ReadinessVerdict.NeedsWork
            : ReadinessVerdict.Ready;

        var readiness = Math.Max(0, 100 - open.Sum(i => Weight(i.Severity)));
        var risk = Math.Min(100, current.Sum(i => Weight(i.Severity)));

        return new ReviewGrade(verdict, readiness, risk, open.Count, critical, high, current.Count - open.Count);
    }
}
