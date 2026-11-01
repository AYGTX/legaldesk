using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.RequestTypes.Queries;

/// <summary>Active request types visible to end users when creating a request (read-only).</summary>
public record GetActiveRequestTypesQuery : IQuery<List<RequestTypeDto>>;

public class GetActiveRequestTypesQueryHandler(
    IApplicationDbContext db) : IQueryHandler<GetActiveRequestTypesQuery, List<RequestTypeDto>>
{
    public async Task<List<RequestTypeDto>> HandleAsync(GetActiveRequestTypesQuery query, CancellationToken cancellationToken = default)
    {
        return await db.RequestTypes.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new RequestTypeDto(t.Id, t.Name, t.Description, t.SlaHours, t.IsActive, t.Icon, t.Color, t.CreatedAt,
                t.DefaultPresetId, db.WorkflowPresets.Where(p => p.Id == t.DefaultPresetId).Select(p => p.Name).FirstOrDefault(),
                t.RuleSetKeys == null ? null : t.RuleSetKeys.Split(',', StringSplitOptions.None).ToList()))
            .ToListAsync(cancellationToken);
    }
}
