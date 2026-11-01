using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Lifecycle;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// The user's calendar feed (R-M10-9): notice deadlines, end dates and obligations as an .ics address a calendar
/// subscribes to. The address carries a secret; issuing a new one or revoking it stops the old address.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/calendar")]
[ApiController]
public class CalendarController : ControllerBase
{
    /// <summary>Issue (or replace) the current user's feed address</summary>
    [HttpPost("feed")]
    [Authorize]
    [ProducesResponseType(typeof(CalendarFeedResponse), 200)]
    public async Task<ActionResult<CalendarFeedResponse>> Issue(
        [FromServices] ICommandHandler<IssueCalendarTokenCommand, string> handler, CancellationToken ct)
    {
        var token = await handler.HandleAsync(new IssueCalendarTokenCommand(), ct);
        return Ok(new CalendarFeedResponse($"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/v1/calendar/{token}.ics"));
    }

    /// <summary>Stop the current user's feed address</summary>
    [HttpDelete("feed")]
    [Authorize]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Revoke([FromServices] ICommandHandler<RevokeCalendarTokenCommand> handler, CancellationToken ct)
    {
        await handler.HandleAsync(new RevokeCalendarTokenCommand(), ct);
        return NoContent();
    }

    /// <summary>The feed, read by a calendar application through its secret address</summary>
    [HttpGet("{token}.ics")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimits.Verify)]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Feed([FromRoute] string token,
        [FromServices] IQueryHandler<GetCalendarFeedQuery, string> handler, CancellationToken ct)
    {
        var ics = await handler.HandleAsync(new GetCalendarFeedQuery(token), ct);
        Response.Headers.CacheControl = "private, max-age=900";
        return File(System.Text.Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8", "legaldesk.ics");
    }
}

public record CalendarFeedResponse(string Url);
