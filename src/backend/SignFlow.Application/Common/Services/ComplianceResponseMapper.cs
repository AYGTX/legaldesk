using System.Text;
using System.Text.Json;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// Turns the language model's compliance JSON back into a <see cref="ComplianceAnalysis"/> over the
/// regulations that were actually sent to it.
///
/// This lives in the Application layer rather than inside the Groq client on purpose. The model
/// answers by <b>position</b> — it is never asked to echo a regulation's GUID, because a model that
/// retypes a 36-character identifier will eventually retype it wrong and silently attach a finding
/// to the wrong regulation. That makes the numbering in the prompt and the un-numbering of the reply
/// two halves of one contract, so both live here where they can be unit-tested and cannot drift
/// apart.
/// </summary>
public static class ComplianceResponseMapper
{
    public const string StatusAddressed = "Addressed";
    public const string StatusGap = "Gap";
    public const string StatusUnclear = "Unclear";

    private const string NotAssessedExplanation =
        "The review did not reach a conclusion on this regulatory text. It has not been checked against the contract.";

    /// <summary>
    /// Renders the regulations as the numbered block the prompt refers to. The 1-based position here
    /// is the "regulationIndex" the model answers with — <see cref="FromModelResponse"/> reverses it.
    /// </summary>
    public static string FormatRegulations(IReadOnlyList<RegulatoryTextSummary> regulations)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < regulations.Count; i++)
        {
            var regulation = regulations[i];
            sb.Append(i + 1).Append(". ").AppendLine(regulation.Title);

            if (!string.IsNullOrWhiteSpace(regulation.Reference))
                sb.Append("   Reference: ").AppendLine(regulation.Reference);

            if (!string.IsNullOrWhiteSpace(regulation.Keywords))
                sb.Append("   Subject matter: ").AppendLine(regulation.Keywords);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Maps the model's reply onto <paramref name="regulations"/>. Every regulation sent is returned
    /// exactly once: one the model skipped comes back as "Unclear" rather than vanishing, so the
    /// finding count always equals the number of texts the contract was checked against and the score
    /// cannot be flattered by an incomplete answer.
    /// </summary>
    public static ComplianceAnalysis FromModelResponse(
        JsonElement root,
        IReadOnlyList<RegulatoryTextSummary> regulations,
        DateTime analyzedAt)
    {
        var byIndex = new Dictionary<int, ComplianceFinding>();

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("findings", out var findingsEl)
            && findingsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var finding in findingsEl.EnumerateArray())
            {
                if (finding.ValueKind != JsonValueKind.Object) continue;

                var index = ReadIndex(finding, "regulationIndex");

                // Out of range means the model invented a regulation that was never sent — drop it
                // rather than guessing which one it meant. First answer wins on a duplicate index.
                if (index is null || index < 1 || index > regulations.Count) continue;
                if (byIndex.ContainsKey(index.Value)) continue;

                var regulation = regulations[index.Value - 1];
                var status = NormalizeStatus(ReadString(finding, "status"));

                byIndex[index.Value] = new ComplianceFinding(
                    regulation.Id,
                    regulation.Title,
                    status,
                    ReadString(finding, "explanation") ?? NotAssessedExplanation,
                    // Severity only means something for a gap; an addressed regulation carrying a
                    // severity chip would read as a problem in the UI.
                    status == StatusGap ? NormalizeSeverity(ReadString(finding, "severity")) : null,
                    ReadString(finding, "whyItMatters"),
                    ReadString(finding, "recommendedAction"),
                    ReadString(finding, "proposedLanguage"),
                    ReadString(finding, "contractReference"));
            }
        }

        var findings = new List<ComplianceFinding>(regulations.Count);
        for (var i = 0; i < regulations.Count; i++)
        {
            findings.Add(byIndex.TryGetValue(i + 1, out var mapped)
                ? mapped
                : new ComplianceFinding(regulations[i].Id, regulations[i].Title, StatusUnclear, NotAssessedExplanation));
        }

        return new ComplianceAnalysis(ScoreOf(findings), findings, analyzedAt, ReadLimitations(root));
    }

    /// <summary>
    /// Percentage of checked regulations the contract addresses. Always recomputed from the findings
    /// rather than read from the model's reply — the score drives the readiness verdict, so it has to
    /// follow from the findings a reader can see.
    /// </summary>
    public static int ScoreOf(IReadOnlyList<ComplianceFinding> findings) =>
        findings.Count == 0
            ? 100
            : (int)Math.Round(100.0 * findings.Count(f => f.Status == StatusAddressed) / findings.Count);

    /// <summary>Anything the model did not answer with one of the three verdicts is treated as unclear.</summary>
    private static string NormalizeStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "addressed" => StatusAddressed,
        "gap" => StatusGap,
        _ => StatusUnclear
    };

    private static string? NormalizeSeverity(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "critical" => "Critical",
        "high" => "High",
        "medium" => "Medium",
        "low" => "Low",
        _ => null
    };

    private static List<string>? ReadLimitations(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("limitations", out var el)
            || el.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var limitations = el.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return limitations.Count == 0 ? null : limitations;
    }

    /// <summary>Accepts the index as a number or as a string — models are inconsistent about which.</summary>
    private static int? ReadIndex(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), out var n) => n,
            _ => null
        };
    }

    private static string? ReadString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
