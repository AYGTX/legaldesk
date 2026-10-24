namespace SignFlow.Domain.Enums;

/// <summary>Who a workflow-preset step asks to approve.</summary>
public enum PresetApproverKind
{
    /// <summary>One named person.</summary>
    User = 0,

    /// <summary>Whoever holds a system role (Administrator, Manager...). Must resolve to exactly one active person.</summary>
    Role = 1,

    /// <summary>Whoever works in a department. Must resolve to exactly one active person.</summary>
    Department = 2
}
