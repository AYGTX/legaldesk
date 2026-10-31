using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.WorkflowPresets;

/// <summary>One step as the admin screen sends it. Which of UserId / RoleName / TeamId counts depends on Kind.</summary>
public record WorkflowPresetStepInput(
    int Stage,
    string? Label,
    PresetApproverKind Kind,
    Guid? UserId,
    string? RoleName,
    Guid? TeamId,
    string? ConditionFieldKey,
    string? ConditionOperator,
    string? ConditionValue);

public record WorkflowPresetStepDto(
    int Order,
    int Stage,
    string? Label,
    PresetApproverKind Kind,
    Guid? UserId,
    string? UserName,
    string? RoleName,
    Guid? TeamId,
    string? TeamName,
    string? ConditionFieldKey,
    string? ConditionOperator,
    string? ConditionValue);

public record WorkflowPresetDto(
    Guid Id,
    string Name,
    string? Description,
    WorkflowType WorkflowType,
    bool IsActive,
    List<WorkflowPresetStepDto> Steps,
    /// <summary>How many templates (HTML and PDF) follow this workflow.</summary>
    int TemplateCount);

/// <summary>One step as the person creating a request sees it: what it is for, and who it will go to.</summary>
public record PresetStepSummaryDto(int Order, string? Label, string Who, string? Condition);

/// <summary>The workflow a template follows, as shown on the template and in the request screen.</summary>
public record TemplatePresetDto(Guid Id, string Name, WorkflowType WorkflowType, bool IsActive, List<PresetStepSummaryDto> Steps);

public static class WorkflowPresetSummaries
{
    public static async Task<TemplatePresetDto?> LoadAsync(IApplicationDbContext db, Guid? presetId, CancellationToken ct)
    {
        if (presetId is null) return null;

        var preset = await db.WorkflowPresets.AsNoTracking().Include(p => p.Steps)
            .FirstOrDefaultAsync(p => p.Id == presetId.Value, ct);
        if (preset is null) return null;

        var userIds = preset.Steps.Where(s => s.ApproverUserId.HasValue).Select(s => s.ApproverUserId!.Value).ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var teamIds = preset.Steps.Where(s => s.TeamId.HasValue).Select(s => s.TeamId!.Value).ToList();
        var teams = await db.Teams.AsNoTracking().Where(t => teamIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return new TemplatePresetDto(
            preset.Id, preset.Name, preset.WorkflowType, preset.IsActive,
            [.. preset.Steps.OrderBy(s => s.Order).Select(s => new PresetStepSummaryDto(
                s.Order, s.Label,
                s.ApproverKind switch
                {
                    PresetApproverKind.User => s.ApproverUserId is { } id && names.TryGetValue(id, out var n) ? n : "?",
                    PresetApproverKind.Role => $"Role: {s.RoleName}",
                    _ => $"Team: {(s.TeamId is { } t && teams.TryGetValue(t, out var tn) ? tn : "?")}"
                },
                string.IsNullOrWhiteSpace(s.ConditionFieldKey)
                    ? null
                    : string.IsNullOrWhiteSpace(s.ConditionOperator)
                        ? s.ConditionFieldKey
                        : $"{s.ConditionFieldKey} {s.ConditionOperator} {s.ConditionValue}"))]);
    }
}
