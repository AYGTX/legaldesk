namespace SignFlow.Application.Common.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string name, object key)
        : base($"Entity '{name}' with key '{key}' was not found.") { }

    public NotFoundException(string message)
        : base(message) { }

    /// <summary>A stable code the client can act on ("model.off"), when the refusal has one.</summary>
    public string? Code { get; private init; }

    public static NotFoundException WithCode(string code, string message) => new(message) { Code = code };
}
