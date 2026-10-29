using SignFlow.Domain.Enums;
using SignFlow.Domain.Exceptions;

namespace SignFlow.Domain.Rules;

/// <summary>
/// The only moves a contract's status may make (rework spec R-M2-5). A move that is not listed is a bug in the
/// caller, so it throws instead of being silently stored.
/// Pending and WaitingForOthers are never targets: "waiting for others" is read from the circuit's steps.
/// </summary>
public static class ContractStateMachine
{
    private static readonly Dictionary<DocumentStatus, DocumentStatus[]> Allowed = new()
    {
        // Draft → Draft is a new version saved while the owner prepares the contract.
        [DocumentStatus.Draft] = [DocumentStatus.Draft, DocumentStatus.InProgress],
        [DocumentStatus.InProgress] =
        [
            DocumentStatus.Completed, DocumentStatus.Rejected, DocumentStatus.ChangesRequested,
            DocumentStatus.Expired, DocumentStatus.Cancelled
        ],
        [DocumentStatus.ChangesRequested] = [DocumentStatus.Draft, DocumentStatus.Cancelled],
        [DocumentStatus.Rejected] = [DocumentStatus.Draft],
        [DocumentStatus.Expired] = [DocumentStatus.InProgress, DocumentStatus.Cancelled],
        [DocumentStatus.Completed] = [],
        [DocumentStatus.Cancelled] = [],
        [DocumentStatus.Pending] = [],
        [DocumentStatus.WaitingForOthers] = [],
    };

    public static bool CanMove(DocumentStatus from, DocumentStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static void EnsureCanMove(DocumentStatus from, DocumentStatus to)
    {
        if (!CanMove(from, to))
            throw new InvalidStatusTransitionException(from, to);
    }
}
