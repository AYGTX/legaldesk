using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Notifications.Commands;
using SignFlow.Application.Features.Notifications.Queries;

namespace SignFlow.Api.Controllers.V1;

/// <summary>In-app notification center for the current user</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/notifications")]
[ApiController]
[Authorize]
public class NotificationsController(
    IQueryHandler<GetMyNotificationsQuery, NotificationsResultDto> getHandler,
    ICommandHandler<MarkNotificationsReadCommand> markReadHandler) : ControllerBase
{
    /// <summary>Get recent notifications and the unread count</summary>
    [HttpGet]
    [ProducesResponseType(typeof(NotificationsResultDto), 200)]
    public async Task<ActionResult<NotificationsResultDto>> GetMine([FromQuery] int take = 30, CancellationToken ct = default)
        => Ok(await getHandler.HandleAsync(new GetMyNotificationsQuery(take), ct));

    /// <summary>Mark a single notification as read</summary>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> MarkRead([FromRoute] Guid id, CancellationToken ct)
    {
        await markReadHandler.HandleAsync(new MarkNotificationsReadCommand(id), ct);
        return NoContent();
    }

    /// <summary>Mark all notifications as read</summary>
    [HttpPost("read-all")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await markReadHandler.HandleAsync(new MarkNotificationsReadCommand(null), ct);
        return NoContent();
    }
}
