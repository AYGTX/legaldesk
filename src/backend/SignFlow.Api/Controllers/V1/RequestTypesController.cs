using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.RequestTypes.Commands;
using SignFlow.Application.Features.RequestTypes.Queries;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// Request type configuration — the central source of truth for request categories and their SLAs.
/// Management is restricted to administrators; active types are readable by any authenticated user.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/request-types")]
[ApiController]
[Authorize]
public class RequestTypesController(
    IQueryHandler<GetRequestTypesQuery, List<RequestTypeDto>> getAllHandler,
    IQueryHandler<GetActiveRequestTypesQuery, List<RequestTypeDto>> getActiveHandler,
    ICommandHandler<CreateRequestTypeCommand, Guid> createHandler,
    ICommandHandler<UpdateRequestTypeCommand> updateHandler,
    ICommandHandler<SetRequestTypeStatusCommand> setStatusHandler,
    ICommandHandler<DeleteRequestTypeCommand> deleteHandler) : ControllerBase
{
    /// <summary>List all request types (administrators only)</summary>
    [HttpGet]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(typeof(List<RequestTypeDto>), 200)]
    public async Task<ActionResult<List<RequestTypeDto>>> GetAll(CancellationToken ct)
        => Ok(await getAllHandler.HandleAsync(new GetRequestTypesQuery(), ct));

    /// <summary>List active request types (for users creating a request)</summary>
    [HttpGet("active")]
    [ProducesResponseType(typeof(List<RequestTypeDto>), 200)]
    public async Task<ActionResult<List<RequestTypeDto>>> GetActive(CancellationToken ct)
        => Ok(await getActiveHandler.HandleAsync(new GetActiveRequestTypesQuery(), ct));

    /// <summary>Create a request type</summary>
    [HttpPost]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(typeof(CreateRequestTypeResponse), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<CreateRequestTypeResponse>> Create([FromBody] CreateRequestTypeRequest request, CancellationToken ct)
    {
        var id = await createHandler.HandleAsync(
            new CreateRequestTypeCommand(request.Name, request.Description, request.SlaHours, request.Icon, request.Color), ct);
        return CreatedAtAction(nameof(GetAll), new { }, new CreateRequestTypeResponse(id));
    }

    /// <summary>Update a request type (affects future requests only)</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateRequestTypeRequest request, CancellationToken ct)
    {
        await updateHandler.HandleAsync(
            new UpdateRequestTypeCommand(id, request.Name, request.Description, request.SlaHours, request.Icon, request.Color), ct);
        return NoContent();
    }

    /// <summary>Activate or deactivate a request type</summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> SetStatus([FromRoute] Guid id, [FromBody] SetStatusRequest request, CancellationToken ct)
    {
        await setStatusHandler.HandleAsync(new SetRequestTypeStatusCommand(id, request.IsActive), ct);
        return NoContent();
    }

    /// <summary>Delete a request type (only if unused)</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        await deleteHandler.HandleAsync(new DeleteRequestTypeCommand(id), ct);
        return NoContent();
    }
}

public record CreateRequestTypeRequest(string Name, string? Description, int SlaHours, string? Icon, string? Color);
public record UpdateRequestTypeRequest(string Name, string? Description, int SlaHours, string? Icon, string? Color);
public record SetStatusRequest(bool IsActive);
public record CreateRequestTypeResponse(Guid Id);
