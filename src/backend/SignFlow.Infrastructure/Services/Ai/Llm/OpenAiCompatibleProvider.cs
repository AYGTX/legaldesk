using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SignFlow.Infrastructure.Services.Ai.Llm;

/// <summary>Groq and Ollama both speak the OpenAI chat-completions protocol; they differ only in where and how.</summary>
public abstract class OpenAiCompatibleProvider(IHttpClientFactory httpFactory, ILogger logger) : ILlmProvider
{
    public const string QuickClient = "llm-quick";
    public const string AnalysisClient = "llm-analysis";

    public abstract string Name { get; }

    public string ModelName => ModelFor(LlmTask.Quick);
    public abstract bool IsConfigured { get; }
    public abstract int AnalysisMaxInputChars { get; }

    protected abstract string Endpoint { get; }
    protected abstract string? ApiKey { get; }
    protected abstract string ModelFor(LlmTask task);
    protected abstract int MaxTokensFor(LlmTask task);

    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var model = ModelFor(request.Task);
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = request.MaxTokens ?? MaxTokensFor(request.Task),
            ["temperature"] = request.Temperature,
            ["stream"] = false,
            ["messages"] = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserMessage }
            }
        };
        // gpt-oss models think before they answer, and that thinking counts against max_tokens: on a short answer
        // (a summary) it can use the whole budget and leave an empty reply. Keep the thinking short.
        if (model.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase))
            body["reasoning_effort"] = "low";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(ApiKey))
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        using var client = httpFactory.CreateClient(request.Task == LlmTask.Analysis ? AnalysisClient : QuickClient);
        using var response = await client.SendAsync(httpRequest, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("{Provider} API error {Status}: {Body}", Name, response.StatusCode, error);
            response.EnsureSuccessStatusCode();
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var choice = doc.RootElement.GetProperty("choices")[0];
        var content = choice.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
            logger.LogWarning("{Provider} returned an empty answer (finish reason: {Reason})", Name,
                choice.TryGetProperty("finish_reason", out var r) ? r.GetString() : "unknown");
        return content;
    }
}
