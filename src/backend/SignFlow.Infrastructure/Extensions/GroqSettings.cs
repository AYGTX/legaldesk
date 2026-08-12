namespace SignFlow.Infrastructure.Extensions;

public class GroqSettings
{
    public string ApiKey   { get; set; } = string.Empty;
    public string Model    { get; set; } = "llama-3.3-70b-versatile";
    public int    MaxTokens { get; set; } = 400;

    // Full contract review — a legal analysis needs far more room, both to read the
    // contract and to write the findings, than a 3-sentence summary does.
    public string AnalysisModel         { get; set; } = "llama-3.3-70b-versatile";
    public int    AnalysisMaxTokens     { get; set; } = 4000;
    public int    AnalysisMaxInputChars { get; set; } = 48_000;
}
