using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Teams;

public record TeamMemberDto(Guid UserId, string Name, string Email, bool IsActive);

public record TeamDto(Guid Id, string Name, string? Description, List<TeamMemberDto> Members);

public record GetTeamsQuery : IQuery<List<TeamDto>>;

/// <summary>Teams of the organisation with their members (R-M4-4): the names any participant editor offers.</summary>
public class GetTeamsQueryHandler(IApplicationDbContext db) : IQueryHandler<GetTeamsQuery, List<TeamDto>>
{
    public async Task<List<TeamDto>> HandleAsync(GetTeamsQuery query, CancellationToken cancellationToken = default)
    {
        var teams = await db.Teams.AsNoTracking().Include(t => t.Members).OrderBy(t => t.Name).ToListAsync(cancellationToken);
        var userIds = teams.SelectMany(t => t.Members).Select(m => m.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken);

        return teams.Select(t => new TeamDto(t.Id, t.Name, t.Description,
            [.. t.Members.Where(m => users.ContainsKey(m.UserId))
                .Select(m => users[m.UserId])
                .OrderBy(u => u.LastName)
                .Select(u => new TeamMemberDto(u.Id, u.FullName, u.Email!, u.IsActive))])).ToList();
    }
}

/// <summary>Creates a team (no <paramref name="Id"/>) or replaces its name, description and members.</summary>
public record SaveTeamCommand(Guid? Id, string Name, string? Description, List<Guid> MemberIds) : ICommand<Guid>;

public class SaveTeamCommandHandler(IApplicationDbContext db) : ICommandHandler<SaveTeamCommand, Guid>
{
    public async Task<Guid> HandleAsync(SaveTeamCommand command, CancellationToken cancellationToken = default)
    {
        var name = (command.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 120)
            throw new Common.Exceptions.ValidationException("A team needs a name of at most 120 characters.");
        if (command.Description is { Length: > 500 })
            throw new Common.Exceptions.ValidationException("The description is limited to 500 characters.");
        if (await db.Teams.AnyAsync(t => t.Name == name && t.Id != command.Id, cancellationToken))
            throw new ConflictException("team.duplicate", $"A team named \"{name}\" already exists.");

        var memberIds = (command.MemberIds ?? []).Distinct().ToList();
        // Users are filtered by organisation: someone of another organisation is simply not found.
        var known = await db.Users.Where(u => memberIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken);
        if (known.Count != memberIds.Count)
            throw new Common.Exceptions.ValidationException("Some of the chosen members are not people of this organisation.");

        Team team;
        if (command.Id is { } id)
        {
            team = await db.Teams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Team), id);
            team.Rename(name, command.Description);
            foreach (var gone in team.Members.Where(m => !memberIds.Contains(m.UserId)).ToList())
                db.TeamMembers.Remove(gone);
        }
        else
        {
            team = Team.Create(name, command.Description);
            db.Teams.Add(team);
        }

        var present = team.Members.Select(m => m.UserId).ToHashSet();
        foreach (var userId in memberIds.Where(u => !present.Contains(u)))
            db.TeamMembers.Add(TeamMember.Of(team.Id, userId));

        await db.SaveChangesAsync(cancellationToken);
        return team.Id;
    }
}

public record DeleteTeamCommand(Guid Id) : ICommand;

/// <summary>A team that a workflow or a contract's circuit still names cannot be deleted.</summary>
public class DeleteTeamCommandHandler(IApplicationDbContext db) : ICommandHandler<DeleteTeamCommand>
{
    public async Task HandleAsync(DeleteTeamCommand command, CancellationToken cancellationToken = default)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Team), command.Id);

        var presets = await db.WorkflowPresetSteps.Where(s => s.TeamId == team.Id)
            .Select(s => s.WorkflowPreset.Name).Distinct().ToListAsync(cancellationToken);
        if (presets.Count > 0)
            throw new ConflictException("team.inUse", $"The workflows {string.Join(", ", presets.Select(p => $"\"{p}\""))} use this team.",
                new Dictionary<string, object?> { ["names"] = string.Join(", ", presets) });
        if (await db.Recipients.AnyAsync(r => r.PoolTeamId == team.Id, cancellationToken))
            throw new ConflictException("team.inUse", "Contracts name this team in their circuit.",
                new Dictionary<string, object?> { ["names"] = "" });

        db.Teams.Remove(team);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public record HolidayDto(Guid Id, DateOnly Date, string Name, bool EveryYear);

public record GetHolidaysQuery : IQuery<List<HolidayDto>>;

public class GetHolidaysQueryHandler(IApplicationDbContext db) : IQueryHandler<GetHolidaysQuery, List<HolidayDto>>
{
    public async Task<List<HolidayDto>> HandleAsync(GetHolidaysQuery query, CancellationToken cancellationToken = default) =>
        (await db.OrganisationHolidays.AsNoTracking().ToListAsync(cancellationToken))
            .OrderBy(h => h.EveryYear ? 0 : 1).ThenBy(h => h.EveryYear ? h.Date.DayOfYear : h.Date.DayNumber)
            .Select(h => new HolidayDto(h.Id, h.Date, h.Name, h.EveryYear))
            .ToList();
}

/// <summary>Adds a day off to the working calendar (R-M4-10). Religious holidays move every year: they are added by date.</summary>
public record AddHolidayCommand(DateOnly Date, string Name, bool EveryYear) : ICommand<Guid>;

public class AddHolidayCommandHandler(IApplicationDbContext db) : ICommandHandler<AddHolidayCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddHolidayCommand command, CancellationToken cancellationToken = default)
    {
        var name = (command.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 120)
            throw new Common.Exceptions.ValidationException("A holiday needs a name of at most 120 characters.");
        var holiday = OrganisationHoliday.On(command.Date, name, command.EveryYear);
        db.OrganisationHolidays.Add(holiday);
        await db.SaveChangesAsync(cancellationToken);
        return holiday.Id;
    }
}

public record DeleteHolidayCommand(Guid Id) : ICommand;

public class DeleteHolidayCommandHandler(IApplicationDbContext db) : ICommandHandler<DeleteHolidayCommand>
{
    public async Task HandleAsync(DeleteHolidayCommand command, CancellationToken cancellationToken = default)
    {
        var holiday = await db.OrganisationHolidays.FirstOrDefaultAsync(h => h.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(OrganisationHoliday), command.Id);
        db.OrganisationHolidays.Remove(holiday);
        await db.SaveChangesAsync(cancellationToken);
    }
}
