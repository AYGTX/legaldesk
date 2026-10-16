using System.Text.Json;

namespace SignFlow.Application.Common.Serialization;

/// <summary>
/// Serialization settings for the contract-intelligence JSON stored on <c>Document.AiRiskAnalysisJson</c>.
/// This column is written outside the MVC pipeline, so the camelCase policy the API applies everywhere
/// else has to be requested explicitly. Every writer shares this instance so they can never drift —
/// the Angular models read the column verbatim. Public because the demo-contract seeder in
/// Infrastructure writes the same columns and must serialize them identically.
/// </summary>
public static class ContractJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
