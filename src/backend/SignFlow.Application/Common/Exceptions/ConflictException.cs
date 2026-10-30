namespace SignFlow.Application.Common.Exceptions;

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }

    public ConflictException(string code, string message) : base(message) => Code = code;

    /// <summary>A stable code the client can act on ("review.locked"), when the conflict has one.</summary>
    public string? Code { get; }
}
