using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.Vacations.Queries;

public record CheckRecipientAvailabilityQuery(string Email) : IQuery<RecipientAvailabilityDto>;

public record RecipientAvailabilityDto(string Email, bool IsOnVacation, DateTime? AvailableOn);

public class CheckRecipientAvailabilityQueryHandler(
    IApplicationDbContext db) : IQueryHandler<CheckRecipientAvailabilityQuery, RecipientAvailabilityDto>
{
    public async Task<RecipientAvailabilityDto> HandleAsync(CheckRecipientAvailabilityQuery query, CancellationToken cancellationToken = default)
    {
        var email = query.Email.Trim().ToLower();
        var today = DateTime.UtcNow.Date;

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email!.ToLower() == email, cancellationToken);

        if (user is null)
            return new RecipientAvailabilityDto(query.Email, false, null);

        var activeVacation = await db.Vacations.AsNoTracking()
            .Where(v => v.UserId == user.Id && v.StartDate <= today && v.EndDate >= today)
            .OrderByDescending(v => v.EndDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeVacation is null)
            return new RecipientAvailabilityDto(query.Email, false, null);

        return new RecipientAvailabilityDto(query.Email, true, activeVacation.EndDate.AddDays(1));
    }
}
