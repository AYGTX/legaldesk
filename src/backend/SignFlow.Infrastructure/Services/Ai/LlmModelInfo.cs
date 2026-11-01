using SignFlow.Application.Common.Interfaces;
using SignFlow.Infrastructure.Services.Ai.Llm;

namespace SignFlow.Infrastructure.Services.Ai;

/// <summary>Which provider and model the contract assistant uses on this server (M12).</summary>
public class LlmModelInfo(ILlmProvider llm) : IModelInfo
{
    public string Provider => llm.Name;
    public string Model => llm.ModelName;
    public bool IsConfigured => llm.IsConfigured;
}
