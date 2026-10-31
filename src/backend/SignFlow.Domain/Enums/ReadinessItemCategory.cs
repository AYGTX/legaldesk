namespace SignFlow.Domain.Enums;

/// <summary>Which analysis produced a readiness to-do item.</summary>
public enum ReadinessItemCategory
{
    /// <summary>From a RiskFinding in the contract risk analysis.</summary>
    Risk = 0,

    /// <summary>Formerly a gap against the regulatory watch (retired, R-M9-6); kept so old items still read.</summary>
    Compliance = 1,

    /// <summary>From the renewal/expiry obligation extraction.</summary>
    Obligation = 2,

    /// <summary>From a structural check on the document itself rather than its wording.</summary>
    Structure = 3
}
