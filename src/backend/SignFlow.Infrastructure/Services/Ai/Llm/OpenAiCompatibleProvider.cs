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
    public abstract bool IsConfigured { get; }
    public abstract int AnalysisMaxInputChars { get; }

    protected abstract string Endpoint { get; }
    protected abstract string? ApiKey { get; }
    protected abstract string ModelFor(LlmTask task);
    protected abstract int MaxTokensFor(LlmTask task);

    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var body = new
        {
            model = ModelFor(request.Task),
            max_tokens = request.MaxTokens ?? MaxTokensFor(request.Task),
            temperature = request.Temperature,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserMessage }
            }
        };

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
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
               ?? string.Empty;
    }
}
