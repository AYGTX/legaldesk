using SignFlow.Api.Filters;
using SignFlow.Application.Common.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Features.Administration.Commands;
using SignFlow.Application.Features.Administration.Queries;
using System.Collections.Generic;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Administration endpoints</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin")]
[ApiController]
[RequirePermission(Permissions.ManageUsers)]
public class AdminController(
    ICommandHandler<UpdateUserCommand> updateUserHandler,
    ICommandHandler<CreateUserCommand, Guid> createUserHandler,
    ICommandHandler<DeleteUserCommand> deleteUserHandler,
    ICommandHandler<ResetUserPasswordCommand> resetPasswordHandler,
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

    /// <summary>List all available roles</summary>
    [HttpGet("roles")]
    [ProducesResponseType(typeof(List<string>), 200)]
    public async Task<ActionResult<List<string>>> GetRoles(CancellationToken ct)
    {
        var result = await getRolesHandler.HandleAsync(new GetRolesQuery(), ct);
        return Ok(result);
    }

    /// <summary>Create a new user</summary>
    [HttpPost("users")]
    [ProducesResponseType(typeof(CreateUserResponse), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<CreateUserResponse>> CreateUser([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var id = await createUserHandler.HandleAsync(
            new CreateUserCommand(request.FirstName, request.LastName, request.Email, request.Password, request.Company, request.Department, request.Title, request.Roles), ct);
        return CreatedAtAction(nameof(GetUsers), new { }, new CreateUserResponse(id));
    }

    /// <summary>Update user details and roles</summary>
    [HttpPut("users/{id:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> UpdateUser([FromRoute] Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        await updateUserHandler.HandleAsync(new UpdateUserCommand(id, request.FirstName, request.LastName, request.Company, request.Department, request.Title, request.IsActive, request.Roles), ct);
        return NoContent();
    }

    /// <summary>Reset a user's password</summary>
    [HttpPost("users/{id:guid}/reset-password")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> ResetPassword([FromRoute] Guid id, [FromBody] AdminResetPasswordRequest request, CancellationToken ct)
    {
        await resetPasswordHandler.HandleAsync(new ResetUserPasswordCommand(id, request.NewPassword), ct);
        return NoContent();
    }

    /// <summary>Delete a user</summary>
    [HttpDelete("users/{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> DeleteUser([FromRoute] Guid id, CancellationToken ct)
    {
        await deleteUserHandler.HandleAsync(new DeleteUserCommand(id), ct);
        return NoContent();
    }
}

public record UpdateUserRequest(string FirstName, string LastName, string? Company, string? Department, string? Title, bool IsActive, List<string> Roles);
public record CreateUserRequest(string FirstName, string LastName, string Email, string Password, string? Company, string? Department, string? Title, List<string> Roles);
public record CreateUserResponse(Guid Id);
public record AdminResetPasswordRequest(string NewPassword);
