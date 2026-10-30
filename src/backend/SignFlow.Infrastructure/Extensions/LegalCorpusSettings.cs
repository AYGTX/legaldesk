namespace SignFlow.Infrastructure.Extensions;

/// <summary>The legal corpus service (src/legal-corpus). Off by default: reviews then run without article text.</summary>
public class LegalCorpusSettings
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8100";
    public string? ApiKey { get; set; }
    /// <summary>Long enough for a review of a long contract (R-M8-9 allows 120 s).</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
