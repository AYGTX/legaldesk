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
    DateTime CreatedAt);

public class GetRequestTypesQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetRequestTypesQuery, List<RequestTypeDto>>
{
    public async Task<List<RequestTypeDto>> HandleAsync(GetRequestTypesQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator"))
            throw new ForbiddenAccessException();

        return await db.RequestTypes.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new RequestTypeDto(t.Id, t.Name, t.Description, t.SlaHours, t.IsActive, t.Icon, t.Color, t.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
