using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Exceptions;

/// <summary>A contract was asked to move to a status its current status does not allow.</summary>
public class InvalidStatusTransitionException(DocumentStatus from, DocumentStatus to)
    : Exception($"A contract cannot move from {from} to {to}.")
{
    public const string Code = "contract.status.transition";

    public DocumentStatus From { get; } = from;
    public DocumentStatus To { get; } = to;
}
