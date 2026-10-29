using System.ComponentModel.DataAnnotations;
using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// A named, reusable approval path: "Legal, then Finance, then the Director if the amount is over 10 000". A template
/// points at one, so the same path is set up once and changed in one place, instead of being re-entered on every template.
///
/// Steps map to the template's approver signature fields in order. A step may name a person, or a role or department
/// that is looked up when a request is created, and may carry a condition on a field the requester fills in.
/// </summary>
public class WorkflowPreset : TenantEntity
{
    [MaxLength(200)]
    public string Name { get; private set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; private set; }

    /// <summary>How the steps run: Sequential, Parallel, or Mixed (steps that share a stage run together).</summary>
    public WorkflowType WorkflowType { get; private set; } = WorkflowType.Sequential;

    public bool IsActive { get; private set; } = true;

    public ICollection<WorkflowPresetStep> Steps { get; private set; } = [];

    private WorkflowPreset() { }

    public static WorkflowPreset Create(string name, string? description, WorkflowType workflowType)
        => new() { Name = name, Description = description, WorkflowType = workflowType };

    public void Update(string name, string? description, WorkflowType workflowType, bool isActive)
    {
        Name = name;
        Description = description;
        WorkflowType = workflowType;
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }
}
