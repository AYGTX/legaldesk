namespace SignFlow.Infrastructure.Extensions;

/// <summary>Which model strategy backs the AI features: "Groq", "Local" or "None".</summary>
public class AiSettings
{
    public string Provider { get; set; } = "Groq";
}

/// <summary>Any OpenAI-compatible server on the organisation's own machine. Defaults target Ollama.</summary>
public class LocalLlmSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "llama3.1";
    public string? AnalysisModel { get; set; }
    public int MaxTokens { get; set; } = 1000;
    public int AnalysisMaxTokens { get; set; } = 4000;
    // Local models usually run with a smaller context window than hosted ones.
    public int AnalysisMaxInputChars { get; set; } = 24_000;
    public int AnalysisTimeoutSeconds { get; set; } = 300;
}
