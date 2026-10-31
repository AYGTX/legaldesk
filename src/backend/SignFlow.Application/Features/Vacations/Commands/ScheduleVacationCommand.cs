using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Vacations.Queries;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Vacations.Commands;

/// <param name="DelegateUserId">Who acts for this person while they are away (R-M1-8): steps that open for them go to this colleague.</param>
public record ScheduleVacationCommand(DateTime StartDate, DateTime EndDate, string? Note, Guid? DelegateUserId = null) : ICommand<VacationDto>;

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

        if (command.DelegateUserId is { } delegateId)
        {
            if (delegateId == userId)
                throw new Common.Exceptions.ValidationException("Choose a colleague to act for you, not yourself.");
            if (!await db.Users.AnyAsync(u => u.Id == delegateId && u.IsActive, cancellationToken))
                throw new Common.Exceptions.ValidationException("The colleague who acts for you must be an active person of this organisation.");
        }

        var vacation = Vacation.Create(userId, start, end, command.Note);
        vacation.SetDelegate(command.DelegateUserId);
        vacation.SetCreatedBy(userId.ToString());
        db.Vacations.Add(vacation);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} scheduled vacation {Start}–{End}", userId, start, end);

        var today = DateTime.UtcNow.Date;
        var delegateName = command.DelegateUserId is { } d
            ? await db.Users.Where(u => u.Id == d).Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefaultAsync(cancellationToken)
            : null;
        return new VacationDto(vacation.Id, vacation.StartDate, vacation.EndDate, vacation.Note,
            vacation.StartDate <= today && vacation.EndDate >= today, vacation.DelegateUserId, delegateName);
    }
}
