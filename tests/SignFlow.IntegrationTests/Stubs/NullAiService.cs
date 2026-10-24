using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

internal class NullAiService : IAiService
{
    public virtual Task<ContractAnswer> AnswerQuestionAsync(string text, string title, IReadOnlyList<ChatTurn> history, string question, string language, CancellationToken ct = default) =>
        throw new AiNotConfiguredException();

    public Task<string> SummarizeDocumentAsync(string text, string title, CancellationToken ct = default) => Task.FromResult(string.Empty);
    public Task<GeneratedHtmlTemplate> GenerateHtmlFromTextAsync(string userText, CancellationToken ct = default) => Task.FromResult(new GeneratedHtmlTemplate(string.Empty, "Untitled Template", []));
    public Task<ContractRiskAnalysis> AnalyzeContractRiskAsync(string text, string title, CancellationToken ct = default) => Task.FromResult(new ContractRiskAnalysis(0, [], DateTime.UtcNow));
    public Task<ObligationSuggestion> SuggestObligationAsync(string text, string title, DateTime referenceDate, CancellationToken ct = default) => Task.FromResult(new ObligationSuggestion(null, null, null));
    public Task<ContractAnalysisResult> AnalyzeContractAsync(string text, string title, DateTime referenceDate, CancellationToken ct = default) =>
        Task.FromResult(new ContractAnalysisResult(new ContractRiskAnalysis(0, [], DateTime.UtcNow), new ObligationSuggestion(null, null, null)));
    public Task<ComplianceAnalysis> AnalyzeContractComplianceAsync(string text, string title, IReadOnlyList<RegulatoryTextSummary> activeRegulations, CancellationToken ct = default) =>
        Task.FromResult(new ComplianceAnalysis(100, [], DateTime.UtcNow));
    public Task<ComplianceAnalysis> ReviewComplianceAsync(string text, string title, IReadOnlyList<RegulatoryTextSummary> activeRegulations, IReadOnlyList<LegalArticleContext>? legalArticles = null, CancellationToken ct = default) =>
        Task.FromResult(new ComplianceAnalysis(100, [], DateTime.UtcNow));
}
