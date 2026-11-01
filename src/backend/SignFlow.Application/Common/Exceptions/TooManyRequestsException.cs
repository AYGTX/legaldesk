namespace SignFlow.Application.Common.Exceptions;

/// <summary>A per-person limit was reached (429), with a stable code ("model.limit").</summary>
public class TooManyRequestsException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
