using SignFlow.Api.Filters;
using SignFlow.Application.Common.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Teams;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// Teams of the organisation (R-M4-4) and its working calendar (R-M4-10). Everyone can read them to compose a circuit;
/// administrators change them.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[ApiController]
[Authorize]
public class TeamsController(
    IQueryHandler<GetTeamsQuery, List<TeamDto>> listHandler,
    ICommandHandler<SaveTeamCommand, Guid> saveHandler,
    ICommandHandler<DeleteTeamCommand> deleteHandler,
    IQueryHandler<GetHolidaysQuery, List<HolidayDto>> holidaysHandler,
    ICommandHandler<AddHolidayCommand, Guid> addHolidayHandler,
    ICommandHandler<DeleteHolidayCommand> deleteHolidayHandler) : ControllerBase
{
    /// <summary>Teams with their members</summary>
    [HttpGet("teams")]
    [ProducesResponseType(typeof(List<TeamDto>), 200)]
    public async Task<ActionResult<List<TeamDto>>> GetAll(CancellationToken ct) => Ok(await listHandler.HandleAsync(new GetTeamsQuery(), ct));

    /// <summary>Create a team</summary>
    [HttpPost("teams")]
    [RequirePermission(Permissions.ManageUsers)]
    [ProducesResponseType(typeof(SavedTeamResponse), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<SavedTeamResponse>> Create([FromBody] SaveTeamRequest request, CancellationToken ct)
    {
        var id = await saveHandler.HandleAsync(new SaveTeamCommand(null, request.Name, request.Description, request.MemberIds), ct);
        return CreatedAtAction(nameof(GetAll), new { }, new SavedTeamResponse(id));
    }

    /// <summary>Rename a team or change its members. Open steps of the team are shared by its new members.</summary>
    [HttpPut("teams/{id:guid}")]
    [RequirePermission(Permissions.ManageUsers)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] SaveTeamRequest request, CancellationToken ct)
    {
        await saveHandler.HandleAsync(new SaveTeamCommand(id, request.Name, request.Description, request.MemberIds), ct);
        return NoContent();
    }

    /// <summary>Delete a team that no workflow or circuit names (team.inUse otherwise)</summary>
    [HttpDelete("teams/{id:guid}")]
    [RequirePermission(Permissions.ManageUsers)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        await deleteHandler.HandleAsync(new DeleteTeamCommand(id), ct);
        return NoContent();
    }

    /// <summary>The organisation's holidays: no step falls due on them</summary>
    [HttpGet("holidays")]
    [ProducesResponseType(typeof(List<HolidayDto>), 200)]
    public async Task<ActionResult<List<HolidayDto>>> GetHolidays(CancellationToken ct) =>
        Ok(await holidaysHandler.HandleAsync(new GetHolidaysQuery(), ct));

    /// <summary>Add a holiday (a fixed date every year, or one date)</summary>
    [HttpPost("holidays")]
    [RequirePermission(Permissions.ManageUsers)]
    [ProducesResponseType(typeof(SavedTeamResponse), 201)]
    public async Task<ActionResult<SavedTeamResponse>> AddHoliday([FromBody] AddHolidayRequest request, CancellationToken ct)
    {
        var id = await addHolidayHandler.HandleAsync(new AddHolidayCommand(request.Date, request.Name, request.EveryYear), ct);
        return CreatedAtAction(nameof(GetHolidays), new { }, new SavedTeamResponse(id));
    }

    /// <summary>Remove a holiday</summary>
    [HttpDelete("holidays/{id:guid}")]
    [RequirePermission(Permissions.ManageUsers)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteHoliday([FromRoute] Guid id, CancellationToken ct)
    {
        await deleteHolidayHandler.HandleAsync(new DeleteHolidayCommand(id), ct);
        return NoContent();
    }
}

public record SaveTeamRequest(string Name, string? Description, List<Guid> MemberIds);
public record SavedTeamResponse(Guid Id);
public record AddHolidayRequest(DateOnly Date, string Name, bool EveryYear);
