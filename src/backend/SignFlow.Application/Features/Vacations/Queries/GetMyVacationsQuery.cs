using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.Vacations.Queries;

public record GetMyVacationsQuery : IQuery<List<VacationDto>>;

public record VacationDto(Guid Id, DateTime StartDate, DateTime EndDate, string? Note, bool IsActive, Guid? DelegateUserId = null, string? DelegateName = null);

public class GetMyVacationsQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetMyVacationsQuery, List<VacationDto>>
{
    public async Task<List<VacationDto>> HandleAsync(GetMyVacationsQuery query, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();
        var today = DateTime.UtcNow.Date;

        return await db.Vacations.AsNoTracking()
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.StartDate)
            .Select(v => new VacationDto(v.Id, v.StartDate, v.EndDate, v.Note, v.StartDate <= today && v.EndDate >= today, v.DelegateUserId,
                db.Users.Where(u => u.Id == v.DelegateUserId).Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }
}
