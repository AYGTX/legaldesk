using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

internal sealed class NullLegalCorpusClient : ILegalCorpusClient
{
    public Task<IReadOnlyList<LegalArticleContext>> SearchAsync(string query, int topK, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LegalArticleContext>>([]);

    public Task<LegalPlaybook> GetPlaybookAsync(CancellationToken ct = default) =>
        Task.FromResult(new LegalPlaybook(new Dictionary<string, IReadOnlyList<PlaybookArticle>>(), new Dictionary<string, string>()));

    public Task<IReadOnlyList<CitationCheckResult>> CheckCitationsAsync(
        IReadOnlyList<CitationCheckRequest> citations, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CitationCheckResult>>([]);

    public Task<ClauseClassification> ClassifyAsync(IReadOnlyList<ClauseText> clauses, CancellationToken ct = default) =>
        Task.FromResult(new ClauseClassification(false, []));

    public Task<IReadOnlyList<RiskFinding>> ReviewAsync(IReadOnlyList<ClauseText> clauses, string? language, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RiskFinding>>([]);

    public Task<DetectedFormFields> DetectFormFieldsAsync(Stream pdf, string fileName, CancellationToken ct = default) =>
        Task.FromResult(new DetectedFormFields(0, null, false, []));
}
