namespace SignFlow.Domain.Enums;

/// <summary>
/// Approval state of a clause. Only <see cref="Approved"/> clauses are offered when drafting a
/// contract; <see cref="Deprecated"/> clauses are kept for traceability on contracts already using them.
/// </summary>
public enum ClauseStatus
{
    Draft = 0,
    Approved = 1,
    Deprecated = 2
}
