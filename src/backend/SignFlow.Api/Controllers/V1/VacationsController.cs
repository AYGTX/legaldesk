using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Vacations.Commands;
using SignFlow.Application.Features.Vacations.Queries;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Vacation scheduling — users on vacation cannot be added as recipients</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/vacations")]
[ApiController]
[Authorize]
public class VacationsController(
    IQueryHandler<GetMyVacationsQuery, List<VacationDto>> getMyVacationsHandler,
    IQueryHandler<CheckRecipientAvailabilityQuery, RecipientAvailabilityDto> checkAvailabilityHandler,
    ICommandHandler<ScheduleVacationCommand, VacationDto> scheduleHandler,
    ICommandHandler<CancelVacationCommand> cancelHandler) : ControllerBase
{
    /// <summary>List the current user's scheduled vacations</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<VacationDto>), 200)]
    public async Task<ActionResult<List<VacationDto>>> GetMyVacations(CancellationToken ct)
    {
        var result = await getMyVacationsHandler.HandleAsync(new GetMyVacationsQuery(), ct);
        return Ok(result);
    }

    /// <summary>Check whether an email belongs to a user currently on vacation</summary>
    [HttpGet("availability")]
    [ProducesResponseType(typeof(RecipientAvailabilityDto), 200)]
    public async Task<ActionResult<RecipientAvailabilityDto>> CheckAvailability([FromQuery] string email, CancellationToken ct)
    {
        var result = await checkAvailabilityHandler.HandleAsync(new CheckRecipientAvailabilityQuery(email), ct);
        return Ok(result);
    }

    /// <summary>Schedule a new vacation period</summary>
    [HttpPost]
    [ProducesResponseType(typeof(VacationDto), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<VacationDto>> Schedule([FromBody] ScheduleVacationRequest request, CancellationToken ct)
    {
        var result = await scheduleHandler.HandleAsync(new ScheduleVacationCommand(request.StartDate, request.EndDate, request.Note, request.DelegateUserId), ct);
        return CreatedAtAction(nameof(GetMyVacations), new { }, result);
    }

    /// <summary>Cancel a scheduled vacation</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Cancel([FromRoute] Guid id, CancellationToken ct)
    {
        await cancelHandler.HandleAsync(new CancelVacationCommand(id), ct);
        return NoContent();
    }
}

public record ScheduleVacationRequest(DateTime StartDate, DateTime EndDate, string? Note, Guid? DelegateUserId = null);
