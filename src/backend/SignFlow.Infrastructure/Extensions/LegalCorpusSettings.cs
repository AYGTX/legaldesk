namespace SignFlow.Infrastructure.Extensions;

/// <summary>The legal corpus service (src/legal-corpus). Off by default: reviews then run without article text.</summary>
public class LegalCorpusSettings
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://127.0.0.1:8100";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}
