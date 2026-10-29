using System.ComponentModel.DataAnnotations;
using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class WorkflowPresetStep : TenantEntity
{
    public Guid WorkflowPresetId { get; private set; }
    public WorkflowPreset WorkflowPreset { get; private set; } = null!;

    /// <summary>Position in the path, from 1. Step n goes to the n-th approver signature field of the template.</summary>
    public int Order { get; private set; }

    /// <summary>Only used when the preset is Mixed: steps with the same stage act together.</summary>
    public int Stage { get; private set; } = 1;

    /// <summary>What the step is for ("Legal review"). Shown to the approver as their role on the request.</summary>
    [MaxLength(200)]
    public string? Label { get; private set; }

    public PresetApproverKind ApproverKind { get; private set; }

    /// <summary>The person, when <see cref="ApproverKind"/> is User.</summary>
    public Guid? ApproverUserId { get; private set; }

    /// <summary>The system role, when <see cref="ApproverKind"/> is Role.</summary>
    [MaxLength(100)]
    public string? RoleName { get; private set; }

    /// <summary>The department, when <see cref="ApproverKind"/> is Department.</summary>
    [MaxLength(200)]
    public string? Department { get; private set; }

    /// <summary>
    /// Optional: the step only applies when this field's answer meets the condition (same single comparison as
    /// conditional clauses). A step that does not apply is skipped, together with its signature field.
    /// </summary>
    [MaxLength(100)]
    public string? ConditionFieldKey { get; private set; }

    [MaxLength(2)]
    public string? ConditionOperator { get; private set; }

    [MaxLength(200)]
    public string? ConditionValue { get; private set; }

    private WorkflowPresetStep() { }

    public static WorkflowPresetStep Create(
        Guid presetId, int order, int stage, string? label,
        PresetApproverKind kind, Guid? userId, string? roleName, string? department,
        string? conditionFieldKey, string? conditionOperator, string? conditionValue)
        => new()
        {
            WorkflowPresetId = presetId,
            Order = order,
            Stage = stage < 1 ? 1 : stage,
            Label = label,
            ApproverKind = kind,
            ApproverUserId = kind == PresetApproverKind.User ? userId : null,
            RoleName = kind == PresetApproverKind.Role ? roleName : null,
            Department = kind == PresetApproverKind.Department ? department : null,
            ConditionFieldKey = conditionFieldKey,
            ConditionOperator = conditionFieldKey is null ? null : conditionOperator,
            ConditionValue = conditionFieldKey is null ? null : conditionValue
        };
}
