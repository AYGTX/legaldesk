using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.Vacations.Queries;

/// <summary>The colleagues someone going away can name to act for them (R-M1-8): the organisation's other active
/// members. Any user may read it, unlike the administrators' user list.</summary>
public record GetStandInCandidatesQuery : IQuery<List<StandInCandidateDto>>;

public record StandInCandidateDto(Guid Id, string Name, string Email);

public class GetStandInCandidatesQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetStandInCandidatesQuery, List<StandInCandidateDto>>
{
    public async Task<List<StandInCandidateDto>> HandleAsync(GetStandInCandidatesQuery query, CancellationToken cancellationToken = default)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Id != currentUser.UserId)
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .Take(500)
            .ToListAsync(cancellationToken);
        return [.. users.Select(u => new StandInCandidateDto(u.Id, $"{u.FirstName} {u.LastName}".Trim(), u.Email ?? string.Empty))];
    }
}
