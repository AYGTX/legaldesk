using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Vacations.Queries;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Vacations.Commands;

public record ScheduleVacationCommand(DateTime StartDate, DateTime EndDate, string? Note) : ICommand<VacationDto>;

public class ScheduleVacationCommandValidator : AbstractValidator<ScheduleVacationCommand>
{
    public ScheduleVacationCommandValidator()
    {
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("End date must be on or after the start date.");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public class ScheduleVacationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<ScheduleVacationCommandHandler> logger) : ICommandHandler<ScheduleVacationCommand, VacationDto>
{
    public async Task<VacationDto> HandleAsync(ScheduleVacationCommand command, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();

        var start = command.StartDate.Date;
        var end = command.EndDate.Date;

        // Prevent overlapping vacation periods for the same user.
        var overlaps = await db.Vacations
            .AnyAsync(v => v.UserId == userId && start <= v.EndDate && end >= v.StartDate, cancellationToken);
        if (overlaps)
            throw new ConflictException("This period overlaps with an existing scheduled vacation.");

        var vacation = Vacation.Create(userId, start, end, command.Note);
        vacation.SetCreatedBy(userId.ToString());
        db.Vacations.Add(vacation);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} scheduled vacation {Start}–{End}", userId, start, end);

        var today = DateTime.UtcNow.Date;
        return new VacationDto(vacation.Id, vacation.StartDate, vacation.EndDate, vacation.Note,
            vacation.StartDate <= today && vacation.EndDate >= today);
    }
}
