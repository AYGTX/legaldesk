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
/// <param name="Approver">The person the step resolved to. Null only when <paramref name="Skipped"/>.</param>
/// <param name="Label">The step's name, kept on the recipient as their role on the request.</param>
/// <param name="Stage">The step's stage (used when the preset is Mixed).</param>
public record PresetAssignment(bool Skipped, ApproverSelection? Approver, string? Label, int Stage);

/// <summary>The preset's verdict for a template: how its recipients run, and one assignment per covered signature field.</summary>
public record PresetRouting(string PresetName, WorkflowType WorkflowType, IReadOnlyList<PresetAssignment> Assignments);

/// <summary>
/// Turns a template's workflow preset into concrete signers at the moment a request is created: looks up the person
/// behind a role or department, and drops the steps whose condition does not hold for this request's answers.
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
    UserManager<ApplicationUser> userManager) : IWorkflowPresetResolver
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

            var user = await ResolveApproverAsync(preset, step, cancellationToken);
            assignments.Add(new PresetAssignment(false, new ApproverSelection(user.Id, null, null), step.Label, step.Stage));
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

    private async Task<ApplicationUser> ResolveApproverAsync(WorkflowPreset preset, WorkflowPresetStep step, CancellationToken ct)
    {
        var where = $"Workflow \"{preset.Name}\", step {step.Order}" + (string.IsNullOrWhiteSpace(step.Label) ? "" : $" ({step.Label})");

        switch (step.ApproverKind)
        {
            case PresetApproverKind.User:
            {
                var user = step.ApproverUserId is { } id ? await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) : null;
                if (user is null || !user.IsActive)
                    throw new Common.Exceptions.ValidationException($"{where}: the named approver no longer exists or is inactive. Update the workflow.");
                return user;
            }

            case PresetApproverKind.Role:
            {
                var role = step.RoleName ?? "";
                var holders = (await userManager.GetUsersInRoleAsync(role)).Where(u => u.IsActive).ToList();
                return ExactlyOne(holders, where, $"the role \"{role}\"");
            }

            default:
            {
                var department = step.Department ?? "";
                var members = await db.Users
                    .Where(u => u.IsActive && u.Department != null && u.Department.ToLower() == department.ToLower())
                    .ToListAsync(ct);
                return ExactlyOne(members, where, $"the department \"{department}\"");
            }
        }
    }

    private static ApplicationUser ExactlyOne(List<ApplicationUser> candidates, string where, string group)
    {
        if (candidates.Count == 1) return candidates[0];

        var detail = candidates.Count == 0
            ? $"{group} has no active person"
            : $"{group} has {candidates.Count} active people ({string.Join(", ", candidates.Take(5).Select(u => u.FullName))}{(candidates.Count > 5 ? "…" : "")})";
        throw new Common.Exceptions.ValidationException(
            $"{where}: {detail}. A role or department step needs exactly one person; name the approver instead.");
    }
}
