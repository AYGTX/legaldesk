namespace SignFlow.Infrastructure.Extensions;

public class GroqSettings
{
    public string ApiKey   { get; set; } = string.Empty;
    public string Model    { get; set; } = "llama-3.3-70b-versatile";
    public int    MaxTokens { get; set; } = 400;
}
