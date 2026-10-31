namespace SignFlow.Domain.Enums;

/// <summary>Who a workflow-preset step asks to approve.</summary>
public enum PresetApproverKind
{
    /// <summary>One named person.</summary>
    User = 0,

    /// <summary>Everyone who holds a system role (Administrator, Manager...); any one of them acts for it (R-M4-3).</summary>
    Role = 1,

    /// <summary>Every member of a team; any one of them acts for it (R-M4-3).</summary>
    Team = 2
}
