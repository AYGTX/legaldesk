using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignFlow.Infrastructure.Extensions;

namespace SignFlow.Infrastructure.Services.Ai.Llm;

public class GroqLlmProvider(IOptions<GroqSettings> opts, IHttpClientFactory httpFactory, ILogger<GroqLlmProvider> logger)
    : OpenAiCompatibleProvider(httpFactory, logger)
{
    private GroqSettings Settings => opts.Value;

    public override string Name => "Groq";
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.ApiKey);
    public override int AnalysisMaxInputChars => Settings.AnalysisMaxInputChars;

    protected override string Endpoint => "https://api.groq.com/openai/v1/chat/completions";
    protected override string? ApiKey => Settings.ApiKey;
    protected override string ModelFor(LlmTask task) => task == LlmTask.Analysis ? Settings.AnalysisModel : Settings.Model;
    protected override int MaxTokensFor(LlmTask task) => task == LlmTask.Analysis ? Settings.AnalysisMaxTokens : Settings.MaxTokens;
}
