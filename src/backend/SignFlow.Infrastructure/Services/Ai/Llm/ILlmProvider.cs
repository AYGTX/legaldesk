namespace SignFlow.Infrastructure.Services.Ai.Llm;

/// <summary>Quick = summaries and template drafts (fail fast); Analysis = full legal reviews (slow, large output).</summary>
public enum LlmTask
{
    Quick,
    Analysis
}

public record LlmRequest(string SystemPrompt, string UserMessage, LlmTask Task, double Temperature, int? MaxTokens = null);

/// <summary>
/// Strategy for the language model behind <see cref="LlmAiService"/>. The prompts and the parsing stay in the
/// service; a provider only knows how to send one chat turn to its model.
/// </summary>
public interface ILlmProvider
{
    string Name { get; }

    /// <summary>The model that answers ordinary requests, named in the audit and on every output (R-M12-2, R-M12-3).</summary>
    string ModelName { get; }

    /// <summary>False when no model is configured — callers then use the rules-based paths.</summary>
    bool IsConfigured { get; }

    /// <summary>How much contract text a full review may send. Smaller local models need less.</summary>
    int AnalysisMaxInputChars { get; }

    Task<string> CompleteAsync(LlmRequest request, CancellationToken ct);
}
