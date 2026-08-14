using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// One to-do item the pre-flight review wants to create, before it is matched against what is
/// already stored for the document.
/// </summary>
public record ReadinessItemDraft(
    ReadinessItemCategory Category,
    string Severity,
    string SourceKey,
    string Title,
    string Detail,
    string? WhyItMatters,
    string? RecommendedAction,
    string? ProposedLanguage);

/// <summary>
/// The graded outcome. Deliberately carries counts rather than a prose sentence: the wording is
/// built in the UI so it comes out in the reader's language (see the bilingual rule for this app).
/// </summary>
public record ReadinessAssessment(
    ReadinessVerdict Verdict,
    int Score,
    int OpenCount,
    int CriticalCount,
    int HighCount,
    int HandledCount);

/// <summary>
/// Turns the risk and compliance analyses into a single readiness verdict plus the list of
/// adjustments that would make the contract sendable.
///
/// This is deliberately a **deterministic rule set over findings the model already produced**, not
/// a second AI call: the verdict has to be reproducible, has to recompute instantly when an owner
/// ticks an item off, and has to be explainable line by line if a lawyer asks why a contract was
/// flagged. It is not a legal opinion.
/// </summary>
public static class ReadinessEvaluator
{
    private const string Critical = "Critical";
    private const string High = "High";

    /// <summary>Builds the to-do drafts from the raw analyses. Order: most severe first.</summary>
    public static List<ReadinessItemDraft> BuildItems(
        ContractRiskAnalysis? risk,
        ComplianceAnalysis? compliance)
    {
        var items = new List<ReadinessItemDraft>();

        if (risk is not null)
        {
            foreach (var f in risk.Findings)
            {
                items.Add(new ReadinessItemDraft(
                    ReadinessItemCategory.Risk,
                    Normalise(f.Severity),
                    $"risk:{f.ClauseType}",
                    f.ClauseType,
                    f.Explanation,
                    f.WhyItMatters,
                    f.RecommendedAction,
                    f.ProposedLanguage));
            }
        }

        if (compliance is not null)
        {
            // Only gaps are actionable — an "Addressed" regulation needs no adjustment, and an
            // "Unclear" one is a limitation of the analysis rather than a defect in the contract.
            foreach (var f in compliance.Findings.Where(f => f.Status == "Gap"))
            {
                items.Add(new ReadinessItemDraft(
                    ReadinessItemCategory.Compliance,
                    // The model review grades each gap; the keyword scan cannot, and its gaps stay
                    // High as they always have.
                    f.Severity is null ? High : Normalise(f.Severity),
                    $"compliance:{f.RegulationId}",
                    f.RegulationTitle,
                    f.Explanation,
                    f.WhyItMatters,
                    f.RecommendedAction,
                    f.ProposedLanguage));
            }
        }

        return [.. items.OrderBy(i => SeverityRank(i.Severity))];
    }

    /// <summary>
    /// Scores and grades the contract from the items that are still <b>open</b>, so resolving or
    /// dismissing an item moves the verdict without re-running any analysis.
    /// </summary>
    public static ReadinessAssessment Assess(
        int totalCount,
        IReadOnlyList<string> openSeverities,
        int? riskScore,
        int? complianceScore)
    {
        var criticalCount = openSeverities.Count(s => s == Critical);
        var highCount = openSeverities.Count(s => s == High);

        var verdict = criticalCount > 0 ? ReadinessVerdict.Blocked
            : highCount > 0 ? ReadinessVerdict.NeedsWork
            : ReadinessVerdict.Ready;

        // Two thirds of the score is how clean the drafting is (inverted risk), one third is how
        // well it covers the regulations the organisation tracks. Unknown halves score neutrally.
        var drafting = 100 - Math.Clamp(riskScore ?? 50, 0, 100);
        var coverage = Math.Clamp(complianceScore ?? 50, 0, 100);
        var score = (int)Math.Round(drafting * 0.65 + coverage * 0.35);

        // Open findings weigh on the headline number even when the stored risk score is stale.
        score -= criticalCount * 15 + highCount * 5;
        score = Math.Clamp(score, 0, 100);

        return new ReadinessAssessment(
            verdict,
            score,
            openSeverities.Count,
            criticalCount,
            highCount,
            totalCount - openSeverities.Count);
    }

    private static string Normalise(string severity) => severity switch
    {
        "Critical" or "High" or "Medium" or "Low" => severity,
        _ => "Medium"
    };

    private static int SeverityRank(string severity) => severity switch
    {
        Critical => 0,
        High => 1,
        "Medium" => 2,
        _ => 3
    };
}
