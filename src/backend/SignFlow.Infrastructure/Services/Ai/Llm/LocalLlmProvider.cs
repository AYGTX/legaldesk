using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignFlow.Infrastructure.Extensions;

namespace SignFlow.Infrastructure.Services.Ai.Llm;

/// <summary>A model on the organisation's own machine (Ollama by default): contract text never leaves it.</summary>
public class LocalLlmProvider(IOptions<LocalLlmSettings> opts, IHttpClientFactory httpFactory, ILogger<LocalLlmProvider> logger)
    : OpenAiCompatibleProvider(httpFactory, logger)
{
    private LocalLlmSettings Settings => opts.Value;

    public override string Name => "Local";
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.BaseUrl) && !string.IsNullOrWhiteSpace(Settings.Model);
    public override int AnalysisMaxInputChars => Settings.AnalysisMaxInputChars;

    protected override string Endpoint => $"{Settings.BaseUrl.TrimEnd('/')}/chat/completions";
    protected override string? ApiKey => Settings.ApiKey;
    protected override string ModelFor(LlmTask task) =>
        task == LlmTask.Analysis && !string.IsNullOrWhiteSpace(Settings.AnalysisModel) ? Settings.AnalysisModel : Settings.Model;
    protected override int MaxTokensFor(LlmTask task) => task == LlmTask.Analysis ? Settings.AnalysisMaxTokens : Settings.MaxTokens;
}
