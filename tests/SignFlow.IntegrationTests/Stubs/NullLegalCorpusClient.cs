using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

/// <summary>Harvey up, with an empty grid: every review finds nothing.</summary>
internal sealed class NullLegalCorpusClient : ILegalCorpusClient
{
    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<LegalPlaybook> GetPlaybookAsync(CancellationToken ct = default) =>
        Task.FromResult(new LegalPlaybook(new Dictionary<string, IReadOnlyList<PlaybookArticle>>()));

    public Task<HarveyReview> ReviewAsync(IReadOnlyList<ClauseText> clauses, string? language, IReadOnlyCollection<string> disabledRules,
        IReadOnlyDictionary<string, string> severityOverrides, CancellationToken ct = default) =>
        Task.FromResult(new HarveyReview(language ?? "fr", "empty", []));

    public Task SendFeedbackAsync(ReviewFeedbackReport report, CancellationToken ct = default) => Task.CompletedTask;

    public Task<DetectedFormFields> DetectFormFieldsAsync(Stream pdf, string fileName, CancellationToken ct = default) =>
        Task.FromResult(new DetectedFormFields(0, null, false, []));
}
