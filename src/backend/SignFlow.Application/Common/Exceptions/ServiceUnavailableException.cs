namespace SignFlow.Application.Common.Exceptions;

/// <summary>A service the action depends on (Harvey, the virus scanner…) is down. Answered 503 with <see cref="Code"/>.</summary>
public class ServiceUnavailableException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
