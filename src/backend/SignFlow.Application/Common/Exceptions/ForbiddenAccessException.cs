namespace SignFlow.Application.Common.Exceptions;

public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException()
        : base("You do not have permission to perform this action.") { }

    public ForbiddenAccessException(string message)
        : base(message) { }

    public ForbiddenAccessException(string code, string message)
        : base(message) => Code = code;

    /// <summary>A stable code the client can act on ("clause.ownWording"), when the refusal has one.</summary>
    public string? Code { get; }
}
