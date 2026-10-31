using SignFlow.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.WorkflowPresets.Queries;

/// <summary>Every workflow with its steps. Administrators and managers read them (managers pick one on a template).</summary>
public record GetWorkflowPresetsQuery(bool ActiveOnly = false) : IQuery<List<WorkflowPresetDto>>;

public class GetWorkflowPresetsQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetWorkflowPresetsQuery, List<WorkflowPresetDto>>
{
    public async Task<List<WorkflowPresetDto>> HandleAsync(GetWorkflowPresetsQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ViewWorkflowPresets))
            throw new ForbiddenAccessException();

        var presets = await db.WorkflowPresets.AsNoTracking()
            .Include(p => p.Steps)
            .Where(p => !query.ActiveOnly || p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var userIds = presets.SelectMany(p => p.Steps).Where(s => s.ApproverUserId.HasValue)
            .Select(s => s.ApproverUserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var teams = await db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        var htmlCounts = await db.HtmlTemplates.AsNoTracking().Where(t => t.WorkflowPresetId != null && !t.IsDeleted)
            .GroupBy(t => t.WorkflowPresetId!.Value).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var pdfCounts = await db.PdfTemplates.AsNoTracking().Where(t => t.WorkflowPresetId != null && !t.IsDeleted)
            .GroupBy(t => t.WorkflowPresetId!.Value).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        return presets.Select(p => new WorkflowPresetDto(
            p.Id, p.Name, p.Description, p.WorkflowType, p.IsActive,
            [.. p.Steps.OrderBy(s => s.Order).Select(s => new WorkflowPresetStepDto(
                s.Order, s.Stage, s.Label, s.ApproverKind, s.ApproverUserId,
                s.ApproverUserId.HasValue && names.TryGetValue(s.ApproverUserId.Value, out var n) ? n : null,
                s.RoleName, s.TeamId, s.TeamId.HasValue && teams.TryGetValue(s.TeamId.Value, out var tn) ? tn : null,
                s.ConditionFieldKey, s.ConditionOperator, s.ConditionValue))],
            htmlCounts.GetValueOrDefault(p.Id) + pdfCounts.GetValueOrDefault(p.Id))).ToList();
    }
}
