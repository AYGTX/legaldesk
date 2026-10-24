using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.WorkflowPresets;
using SignFlow.Application.Features.WorkflowPresets.Commands;
using SignFlow.Application.Features.WorkflowPresets.Queries;
using SignFlow.Domain.Enums;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// Reusable approval workflows. A template follows one, so the path is set up once and changed in one place.
/// Administrators manage them; administrators and managers can read them to pick one for a template.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/workflow-presets")]
[ApiController]
[Authorize]
public class WorkflowPresetsController(
    IQueryHandler<GetWorkflowPresetsQuery, List<WorkflowPresetDto>> listHandler,
    ICommandHandler<CreateWorkflowPresetCommand, Guid> createHandler,
    ICommandHandler<UpdateWorkflowPresetCommand> updateHandler,
    ICommandHandler<DeleteWorkflowPresetCommand> deleteHandler) : ControllerBase
{
    /// <summary>List workflows with their steps (administrators and managers)</summary>
    [HttpGet]
    [Authorize(Roles = "Administrator,Manager")]
    [ProducesResponseType(typeof(List<WorkflowPresetDto>), 200)]
    public async Task<ActionResult<List<WorkflowPresetDto>>> GetAll([FromQuery] bool activeOnly = false, CancellationToken ct = default)
        => Ok(await listHandler.HandleAsync(new GetWorkflowPresetsQuery(activeOnly), ct));

    /// <summary>Create a workflow (administrators only)</summary>
    [HttpPost]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(typeof(CreateWorkflowPresetResponse), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<CreateWorkflowPresetResponse>> Create([FromBody] SaveWorkflowPresetRequest request, CancellationToken ct)
    {
        var id = await createHandler.HandleAsync(
            new CreateWorkflowPresetCommand(request.Name, request.Description, request.WorkflowType, request.Steps), ct);
        return CreatedAtAction(nameof(GetAll), new { }, new CreateWorkflowPresetResponse(id));
    }

    /// <summary>Change a workflow. Requests already created keep their approvers; new ones follow the change.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] SaveWorkflowPresetRequest request, CancellationToken ct)
    {
        await updateHandler.HandleAsync(
            new UpdateWorkflowPresetCommand(id, request.Name, request.Description, request.WorkflowType, request.IsActive, request.Steps), ct);
        return NoContent();
    }

    /// <summary>Delete a workflow no template follows</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        await deleteHandler.HandleAsync(new DeleteWorkflowPresetCommand(id), ct);
        return NoContent();
    }
}

public record SaveWorkflowPresetRequest(
    string Name,
    string? Description,
    WorkflowType WorkflowType,
    bool IsActive,
    List<WorkflowPresetStepInput> Steps);

public record CreateWorkflowPresetResponse(Guid Id);
