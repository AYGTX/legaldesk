using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

internal class NullAiService : IAiService
{
    public virtual Task<ContractAnswer> AnswerQuestionAsync(string text, string title, IReadOnlyList<ChatTurn> history, string question, string language, CancellationToken ct = default) =>
        throw new AiNotConfiguredException();

    public Task<string> SummarizeDocumentAsync(string text, string title, CancellationToken ct = default) => Task.FromResult(string.Empty);
    public Task<GeneratedHtmlTemplate> GenerateHtmlFromTextAsync(string userText, CancellationToken ct = default) => Task.FromResult(new GeneratedHtmlTemplate(string.Empty, "Untitled Template", []));
    public Task<ObligationSuggestion> SuggestObligationAsync(string text, string title, DateTime referenceDate, CancellationToken ct = default) => Task.FromResult(new ObligationSuggestion(null, null, null));
}
