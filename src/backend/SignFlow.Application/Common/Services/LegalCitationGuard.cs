using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// Settles every citation the model made against the legal corpus. A quote that is not in the article word for
/// word is rejected: the finding stays, but it no longer claims a legal basis. The model cannot pass off a
/// paraphrase or an invented sentence as the law, whatever provider wrote it.
/// </summary>
public static class LegalCitationGuard
{
    public const string Verified = "Verified";
    public const string Unverified = "Unverified";
    public const string Rejected = "Rejected";

    public static IReadOnlyList<(int FindingIndex, CitationCheckRequest Request)> PendingChecks(ComplianceAnalysis analysis) =>
        analysis.Findings
            .Select((finding, index) => (finding, index))
            .Where(x => x.finding.CitationStatus == ComplianceResponseMapper.CitationPending
                        && x.finding.LegalArticleId is not null)
            .Select(x => (x.index, new CitationCheckRequest(x.finding.LegalArticleId!, x.finding.LegalQuote ?? string.Empty)))
            .ToList();

    public static ComplianceAnalysis Apply(
        ComplianceAnalysis analysis,
        IReadOnlyList<(int FindingIndex, CitationCheckRequest Request)> checks,
        IReadOnlyList<CitationCheckResult> results)
    {
        var findings = analysis.Findings.ToList();
        var rejected = 0;

        for (var i = 0; i < checks.Count; i++)
        {
            var index = checks[i].FindingIndex;
            var result = i < results.Count ? results[i] : null;
            var finding = findings[index];

            if (result is { Found: true })
            {
                findings[index] = finding with
                {
                    LegalCitation = result.Citation ?? finding.LegalCitation,
                    CitationStatus = result.IsVerified ? Verified : Unverified
                };
            }
            else
            {
                rejected++;
                findings[index] = finding with { CitationStatus = Rejected };
            }
        }

        var limitations = analysis.Limitations?.ToList() ?? [];
        if (rejected > 0)
            limitations.Add($"{rejected} legal citation(s) proposed by the review were not found word for word in the cited article and were rejected. Those findings have no confirmed legal basis.");

        return analysis with { Findings = findings, Limitations = limitations.Count > 0 ? limitations : null };
    }

    /// <summary>The corpus could not be reached: nothing was confirmed, so nothing may be presented as confirmed.</summary>
    public static ComplianceAnalysis MarkUnchecked(ComplianceAnalysis analysis)
    {
        var findings = analysis.Findings
            .Select(f => f.CitationStatus == ComplianceResponseMapper.CitationPending ? f with { CitationStatus = Unverified } : f)
            .ToList();

        var limitations = analysis.Limitations?.ToList() ?? [];
        limitations.Add("The legal corpus could not be reached to check the citations. Treat the cited articles as unconfirmed.");
        return analysis with { Findings = findings, Limitations = limitations };
    }
}
