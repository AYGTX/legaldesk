namespace SignFlow.Infrastructure.Services.Ai.Llm;

/// <summary>AI switched off: every model-backed feature runs its rules-based version.</summary>
public class NoLlmProvider : ILlmProvider
{
    public string Name => "None";
    public string ModelName => "none";
    public bool IsConfigured => false;
    public int AnalysisMaxInputChars => 0;

    public Task<string> CompleteAsync(LlmRequest request, CancellationToken ct) =>
        throw new InvalidOperationException("No language model is configured (Ai:Provider = None).");
}
