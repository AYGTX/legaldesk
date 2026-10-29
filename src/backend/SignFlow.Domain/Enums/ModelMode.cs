namespace SignFlow.Domain.Enums;

/// <summary>Whether an organisation's contracts may be sent to a language model, and which (D3).</summary>
public enum ModelMode
{
    Off = 0,
    Groq = 1,
    Local = 2
}
