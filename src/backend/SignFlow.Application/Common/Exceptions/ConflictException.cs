namespace SignFlow.Application.Common.Exceptions;

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }

    public ConflictException(string code, string message) : base(message) => Code = code;

    public ConflictException(string code, string message, IReadOnlyDictionary<string, object?> parameters) : base(message)
    {
        Code = code;
        Parameters = parameters;
    }

    /// <summary>A stable code the client can act on ("review.locked"), when the conflict has one.</summary>
    public string? Code { get; }

    /// <summary>Values the client's message for <see cref="Code"/> needs ({ step: 2, team: "Legal" }).</summary>
    public IReadOnlyDictionary<string, object?>? Parameters { get; }
}
