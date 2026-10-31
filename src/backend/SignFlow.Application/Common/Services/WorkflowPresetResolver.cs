using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.HtmlTemplates.Commands;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Services;

/// <summary>What a preset decided for one approver signature field of a template.</summary>
/// <param name="Skipped">The step's condition did not hold, so this field gets no signer at all.</param>
/// <param name="Approver">The person a named step resolved to. Null when <paramref name="Skipped"/> or for a pool.</param>
/// <param name="Label">The step's name, kept on the recipient as their role on the request.</param>
/// <param name="Stage">The step's stage (used when the preset is Mixed).</param>
/// <param name="Pool">A team or role step (R-M4-3): any of its members acts for it.</param>
public record PresetAssignment(bool Skipped, ApproverSelection? Approver, string? Label, int Stage, PresetPool? Pool = null);

/// <summary>A team or role a step goes to; <paramref name="Name"/> is shown as the participant.</summary>
public record PresetPool(Guid? TeamId, string? Role, string Name);

/// <summary>The preset's verdict for a template: how its recipients run, and one assignment per covered signature field.</summary>
public record PresetRouting(string PresetName, WorkflowType WorkflowType, IReadOnlyList<PresetAssignment> Assignments);

/// <summary>
/// Turns a template's workflow preset into concrete signers at the moment a request is created: a named person, or a
/// team or role whose members share the step, and drops the steps whose condition does not hold for this request's answers.
/// </summary>
public interface IWorkflowPresetResolver
{
    /// <summary>
    /// Null when the template has no preset. Otherwise one assignment for each of the first
    /// <paramref name="signatureFieldCount"/> steps; further signature fields fall back to the template's own approvers.
    /// </summary>
    Task<PresetRouting?> ResolveAsync(
        Guid? presetId,
        int signatureFieldCount,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, HtmlTemplateFieldType> fieldTypes,
        CancellationToken cancellationToken = default);
}

public class WorkflowPresetResolver(
    IApplicationDbContext db,
    ICircuitDirectory directory) : IWorkflowPresetResolver
{
    public async Task<PresetRouting?> ResolveAsync(
        Guid? presetId,
        int signatureFieldCount,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, HtmlTemplateFieldType> fieldTypes,
        CancellationToken cancellationToken = default)
    {
        if (presetId is null) return null;

        var preset = await db.WorkflowPresets.AsNoTracking()
            .Include(p => p.Steps)
            .FirstOrDefaultAsync(p => p.Id == presetId.Value, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowPreset), presetId.Value);

        if (!preset.IsActive)
            throw new ConflictException($"The workflow \"{preset.Name}\" is no longer active. Choose another one on the template.");

        var assignments = new List<PresetAssignment>();
        foreach (var step in preset.Steps.OrderBy(s => s.Order).Take(signatureFieldCount))
        {
            if (!StepApplies(step, values, fieldTypes))
            {
                assignments.Add(new PresetAssignment(true, null, step.Label, step.Stage));
                continue;
            }

            if (step.ApproverKind == PresetApproverKind.User)
            {
                var user = await ResolveApproverAsync(preset, step, cancellationToken);
                assignments.Add(new PresetAssignment(false, new ApproverSelection(user.Id, null, null), step.Label, step.Stage));
            }
            else
            {
                assignments.Add(new PresetAssignment(false, null, step.Label, step.Stage, await ResolvePoolAsync(preset, step, cancellationToken)));
            }
        }

        return new PresetRouting(preset.Name, preset.WorkflowType, assignments);
    }

    /// <summary>
    /// Whether a step takes part. An answer that is missing or blank cannot rule a step out: dropping an approval
    /// because nobody filled in the amount would let a request skip the very check the condition exists for.
    /// </summary>
    public static bool StepApplies(
        WorkflowPresetStep step,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, HtmlTemplateFieldType> fieldTypes)
    {
        if (string.IsNullOrWhiteSpace(step.ConditionFieldKey)) return true;

        if (!values.TryGetValue(step.ConditionFieldKey, out var answer) || string.IsNullOrWhiteSpace(answer))
            return true;

        var condition = string.IsNullOrWhiteSpace(step.ConditionOperator)
            ? step.ConditionFieldKey
            : $"{step.ConditionFieldKey} {step.ConditionOperator} {step.ConditionValue}";

        return HtmlContractRenderer.Evaluate(condition, values, fieldTypes);
    }

    private static string Where(WorkflowPreset preset, WorkflowPresetStep step) =>
        $"Workflow \"{preset.Name}\", step {step.Order}" + (string.IsNullOrWhiteSpace(step.Label) ? "" : $" ({step.Label})");

    private async Task<ApplicationUser> ResolveApproverAsync(WorkflowPreset preset, WorkflowPresetStep step, CancellationToken ct)
    {
        var user = step.ApproverUserId is { } id ? await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) : null;
        if (user is null || !user.IsActive)
            throw new Common.Exceptions.ValidationException($"{Where(preset, step)}: the named approver no longer exists or is inactive. Update the workflow.");
        return user;
    }

    /// <summary>A team or role with nobody active in it cannot act: refused with preset.step.poolEmpty.</summary>
    private async Task<PresetPool> ResolvePoolAsync(WorkflowPreset preset, WorkflowPresetStep step, CancellationToken ct)
    {
        string name;
        if (step.ApproverKind == PresetApproverKind.Team)
        {
            var team = step.TeamId is { } teamId ? await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId, ct) : null;
            if (team is null)
                throw new Common.Exceptions.ValidationException($"{Where(preset, step)}: its team no longer exists. Update the workflow.");
            name = team.Name;
        }
        else
        {
            name = step.RoleName ?? "";
        }

        var members = await directory.PoolMembersAsync(step.ApproverKind == PresetApproverKind.Team ? step.TeamId : null,
            step.ApproverKind == PresetApproverKind.Role ? step.RoleName : null, ct);
        if (members.Count == 0)
            throw new ConflictException("preset.step.poolEmpty",
                $"{Where(preset, step)}: \"{name}\" has no active member, so nobody could act for it. Add someone to it or change the workflow.",
                new Dictionary<string, object?> { ["step"] = step.Order, ["team"] = name });

        return new PresetPool(step.ApproverKind == PresetApproverKind.Team ? step.TeamId : null,
            step.ApproverKind == PresetApproverKind.Role ? step.RoleName : null, name);
    }
}
