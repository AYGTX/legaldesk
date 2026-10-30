using SignFlow.Api.Filters;
using SignFlow.Application.Common.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Features.Administration.Commands;
using SignFlow.Application.Features.Administration.Queries;
using SignFlow.Application.Features.Auth.Commands;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Administration endpoints: people join by invitation and leave by deactivation (R-M1-1)</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin")]
[ApiController]
[RequirePermission(Permissions.ManageUsers)]
public class AdminController(
    ICommandHandler<UpdateUserCommand> updateUserHandler,
    ICommandHandler<ResetUserPasswordCommand> resetPasswordHandler,
    ICommandHandler<InviteUserCommand, InvitationDto> inviteHandler,
    ICommandHandler<RevokeInvitationCommand> revokeInvitationHandler,
    IQueryHandler<GetInvitationsQuery, List<InvitationDto>> invitationsHandler,
    IQueryHandler<GetUsersQuery, PagedResult<UserDto>> getUsersHandler,
    IQueryHandler<GetRolesQuery, List<string>> getRolesHandler) : ControllerBase
{
    /// <summary>List all users</summary>
    [HttpGet("users")]
    [ProducesResponseType(typeof(PagedResult<UserDto>), 200)]
    public async Task<ActionResult<PagedResult<UserDto>>> GetUsers(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var result = await getUsersHandler.HandleAsync(new GetUsersQuery(pageNumber, pageSize, search), ct);
        return Ok(result);
    }

    /// <summary>List the roles a user can be given</summary>
    [HttpGet("roles")]
    [ProducesResponseType(typeof(List<string>), 200)]
    public async Task<ActionResult<List<string>>> GetRoles(CancellationToken ct)
    {
        var result = await getRolesHandler.HandleAsync(new GetRolesQuery(), ct);
        return Ok(result);
    }

    /// <summary>Update user details, roles and active state (deactivating signs the user out everywhere)</summary>
    [HttpPut("users/{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> UpdateUser([FromRoute] Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        await updateUserHandler.HandleAsync(new UpdateUserCommand(id, request.FirstName, request.LastName, request.Company, request.Department, request.Title, request.IsActive, request.Roles), ct);
        return NoContent();
    }

    /// <summary>Sign the user out everywhere and e-mail them a reset link; nobody else learns the new password</summary>
    [HttpPost("users/{id:guid}/reset-password")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ResetPassword([FromRoute] Guid id, CancellationToken ct)
    {
        await resetPasswordHandler.HandleAsync(new ResetUserPasswordCommand(id), ct);
        return NoContent();
    }

    /// <summary>Invitations of the organisation, newest first</summary>
    [HttpGet("invitations")]
    [ProducesResponseType(typeof(List<InvitationDto>), 200)]
    public async Task<ActionResult<List<InvitationDto>>> GetInvitations(CancellationToken ct) =>
        Ok(await invitationsHandler.HandleAsync(new GetInvitationsQuery(), ct));

    /// <summary>Invite someone by e-mail; the link works once, for 72 hours</summary>
    [HttpPost("invitations")]
    [ProducesResponseType(typeof(InvitationDto), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<InvitationDto>> Invite([FromBody] InviteUserCommand command, CancellationToken ct) =>
        StatusCode(201, await inviteHandler.HandleAsync(command, ct));

    /// <summary>Withdraw an invitation that has not been accepted</summary>
    [HttpDelete("invitations/{id:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> RevokeInvitation([FromRoute] Guid id, CancellationToken ct)
    {
        await revokeInvitationHandler.HandleAsync(new RevokeInvitationCommand(id), ct);
        return NoContent();
    }
}

/// <summary>New details for a user</summary>
public record UpdateUserRequest(string FirstName, string LastName, string? Company, string? Department, string? Title, bool IsActive, List<string> Roles);
