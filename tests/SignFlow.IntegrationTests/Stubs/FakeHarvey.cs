using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

/// <summary>
/// Harvey for scenarios: returns the findings a test sets, applies the organisation's settings the way Harvey does
/// (switched-off rules dropped, severities replaced), and can be "down". Records what it was sent.
/// </summary>
public sealed class FakeHarvey : ILegalCorpusClient
{
    public bool Down { get; set; }
    /// <summary>How long a review takes; a review longer than the run's timeout is cancelled.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    public List<HarveyFinding> Findings { get; set; } = [];
    public string GridVersion { get; set; } = "grid-1";
    public Dictionary<string, IReadOnlyList<PlaybookArticle>> Articles { get; } = [];
    public Dictionary<string, RuleApproval> Approvals { get; } = [];
    public List<ReviewFeedbackReport> Feedback { get; } = [];
    public List<IReadOnlyCollection<string>> DisabledSent { get; } = [];
    public List<ClauseText> ClausesSent { get; } = [];

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(!Down);

    /// <summary>The rules in service; by default one, plus every rule a finding names. Empty it to test an unapproved grid.</summary>
    public HashSet<string> RulesInService { get; } = ["default_rule"];

    public Task<LegalPlaybook> GetPlaybookAsync(CancellationToken ct = default)
    {
        if (Down) throw new HttpRequestException("Harvey is down");
        var rules = RulesInService.Concat(Findings.Select(f => f.RuleKey).OfType<string>()).Concat(Articles.Keys)
            .Distinct()
            .ToDictionary(k => k, k => Articles.TryGetValue(k, out var a) ? a : (IReadOnlyList<PlaybookArticle>)[]);
        return Task.FromResult(new LegalPlaybook(rules, new Dictionary<string, ModelClause>(), Approvals));
    }

    public async Task<HarveyReview> ReviewAsync(IReadOnlyList<ClauseText> clauses, string? language, IReadOnlyCollection<string> disabledRules,
        IReadOnlyDictionary<string, string> severityOverrides, CancellationToken ct = default)
    {
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        if (Down) throw new HttpRequestException("Harvey is down");
        DisabledSent.Add(disabledRules);
        ClausesSent.AddRange(clauses);
        var findings = Findings
            .Where(f => f.RuleKey is null || !disabledRules.Contains(f.RuleKey))
            .Select(f => f.RuleKey is not null && severityOverrides.TryGetValue(f.RuleKey, out var s) ? f with { Severity = s } : f)
            .ToList();
        return new HarveyReview(language ?? "fr", GridVersion, findings);
    }

    public Task SendFeedbackAsync(ReviewFeedbackReport report, CancellationToken ct = default)
    {
        if (Down) throw new HttpRequestException("Harvey is down");
        Feedback.Add(report);
        return Task.CompletedTask;
    }

    public Task<DetectedFormFields> DetectFormFieldsAsync(Stream pdf, string fileName, CancellationToken ct = default) =>
        Task.FromResult(new DetectedFormFields(0, null, false, []));

    public static HarveyFinding Finding(string ruleKey, string severity, string clause, string quote = "les mots du contrat",
        string detection = "CodeCheck", int version = 1) =>
        new(ruleKey, $"Risque {ruleKey}", severity, $"Explication {ruleKey}", "Corriger la clause.", clause, null, version,
            detection, detection == "MeaningMatch" ? 0.82 : null, quote);
}
