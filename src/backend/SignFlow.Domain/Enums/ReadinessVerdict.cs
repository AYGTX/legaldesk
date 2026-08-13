namespace SignFlow.Domain.Enums;

/// <summary>
/// Whether a contract is fit to be sent into a validation/signature circuit, derived from the
/// risk and compliance findings that are still open. Deterministic — see ReadinessEvaluator.
/// </summary>
public enum ReadinessVerdict
{
    /// <summary>No pre-flight review has been run on the current version of the file.</summary>
    NotEvaluated = 0,

    /// <summary>No open critical or high finding — the contract can go out as drafted.</summary>
    Ready = 1,

    /// <summary>Open high-severity risks or regulatory gaps: sendable, but the owner should confirm.</summary>
    NeedsWork = 2,

    /// <summary>An open critical finding — sending is blocked until it is resolved or dismissed.</summary>
    Blocked = 3
}
