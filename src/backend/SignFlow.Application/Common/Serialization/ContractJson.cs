using System.Text.Json;

namespace SignFlow.Application.Common.Serialization;

/// <summary>
/// Serialization settings for the contract-intelligence JSON stored on <c>Document.AiRiskAnalysisJson</c>.
/// This column is written outside the MVC pipeline, so the camelCase policy the API applies everywhere
/// else has to be requested explicitly. Both writers share this instance so the two can never drift —
/// the Angular models read the column verbatim.
/// </summary>
internal static class ContractJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
