using SignFlow.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.RequestTypes.Queries;

/// <summary>Admin listing of all request types (active and inactive).</summary>
public record GetRequestTypesQuery : IQuery<List<RequestTypeDto>>;

public record RequestTypeDto(
    Guid Id,
    string Name,
    string? Description,
    int SlaHours,
    bool IsActive,
    string? Icon,
    string? Color,
    DateTime CreatedAt,
    Guid? DefaultPresetId = null,
    string? DefaultPresetName = null,
    List<string>? RuleSets = null);

public class GetRequestTypesQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetRequestTypesQuery, List<RequestTypeDto>>
{
    public async Task<List<RequestTypeDto>> HandleAsync(GetRequestTypesQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageRequestTypes))
            throw new ForbiddenAccessException();

        return await db.RequestTypes.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new RequestTypeDto(t.Id, t.Name, t.Description, t.SlaHours, t.IsActive, t.Icon, t.Color, t.CreatedAt,
                t.DefaultPresetId, db.WorkflowPresets.Where(p => p.Id == t.DefaultPresetId).Select(p => p.Name).FirstOrDefault(),
                t.RuleSetKeys == null ? null : t.RuleSetKeys.Split(',', StringSplitOptions.None).ToList()))
            .ToListAsync(cancellationToken);
    }
}
