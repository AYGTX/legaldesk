using SignFlow.Api.Filters;
using SignFlow.Application.Common.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Clauses.Commands;
using SignFlow.Application.Features.Clauses.Queries;
using SignFlow.Domain.Enums;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// The shared clause library — legally vetted, reusable contract wording. Curation is restricted
/// to administrators and managers; approved clauses are readable by anyone drafting a contract.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clauses")]
[ApiController]
[Authorize]
public class ClausesController(
    IQueryHandler<GetClausesQuery, List<ClauseDto>> getAllHandler,
    IQueryHandler<GetApprovedClausesQuery, List<ClauseDto>> getApprovedHandler,
    ICommandHandler<CreateClauseCommand, Guid> createHandler,
    ICommandHandler<UpdateClauseCommand> updateHandler,
    ICommandHandler<SetClauseStatusCommand> setStatusHandler,
    ICommandHandler<DeleteClauseCommand> deleteHandler,
    ICommandHandler<RecordClauseUsageCommand> recordUsageHandler) : ControllerBase
{
    /// <summary>List every clause in the library, including drafts (administrators and managers)</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ClauseDto>), 200)]
    public async Task<ActionResult<List<ClauseDto>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] ClauseCategory? category = null,
        [FromQuery] string? language = null,
        [FromQuery] ClauseStatus? status = null,
        CancellationToken ct = default)
        => Ok(await getAllHandler.HandleAsync(new GetClausesQuery(search, category, language, status), ct));

    /// <summary>List approved clauses available for insertion into a contract</summary>
    [HttpGet("approved")]
    [ProducesResponseType(typeof(List<ClauseDto>), 200)]
    public async Task<ActionResult<List<ClauseDto>>> GetApproved(
        [FromQuery] string? search = null,
        [FromQuery] ClauseCategory? category = null,
        [FromQuery] string? language = null,
        [FromQuery] Guid? requestTypeId = null,
        CancellationToken ct = default)
        => Ok(await getApprovedHandler.HandleAsync(new GetApprovedClausesQuery(search, category, language, requestTypeId), ct));

    /// <summary>Add a clause to the library (created as a draft)</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateClauseResponse), 201)]
    public async Task<ActionResult<CreateClauseResponse>> Create([FromBody] CreateClauseRequest request, CancellationToken ct)
    {
        var id = await createHandler.HandleAsync(
            new CreateClauseCommand(request.Title, request.Category, request.Body, request.Language,
                request.Guidance, request.RiskLevel, request.RequestTypeId), ct);
        return CreatedAtAction(nameof(GetAll), new { }, new CreateClauseResponse(id));
    }

    /// <summary>Update clause wording or metadata (bumps the version when the wording changes)</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateClauseRequest request, CancellationToken ct)
    {
        await updateHandler.HandleAsync(
            new UpdateClauseCommand(id, request.Title, request.Category, request.Body, request.Language,
                request.Guidance, request.RiskLevel, request.RequestTypeId), ct);
        return NoContent();
    }

    /// <summary>Approve or deprecate a clause (administrators only)</summary>
    [HttpPatch("{id:guid}/status")]
    [RequirePermission(Permissions.ApproveClauses)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> SetStatus([FromRoute] Guid id, [FromBody] SetClauseStatusRequest request, CancellationToken ct)
    {
        await setStatusHandler.HandleAsync(new SetClauseStatusCommand(id, request.Status), ct);
        return NoContent();
    }

    /// <summary>Record that a clause was inserted into a contract template</summary>
    [HttpPost("{id:guid}/usage")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> RecordUsage([FromRoute] Guid id, CancellationToken ct)
    {
        await recordUsageHandler.HandleAsync(new RecordClauseUsageCommand(id), ct);
        return NoContent();
    }

    /// <summary>Delete a clause that has never been used (administrators only)</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.ApproveClauses)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        await deleteHandler.HandleAsync(new DeleteClauseCommand(id), ct);
        return NoContent();
    }
}

/// <summary>Payload to add a clause to the library.</summary>
public record CreateClauseRequest(
    string Title,
    ClauseCategory Category,
    string Body,
    string Language,
    string? Guidance,
    string? RiskLevel,
    Guid? RequestTypeId);

/// <summary>Payload to update an existing clause.</summary>
public record UpdateClauseRequest(
    string Title,
    ClauseCategory Category,
    string Body,
    string Language,
    string? Guidance,
    string? RiskLevel,
    Guid? RequestTypeId);

/// <summary>Payload to move a clause through its approval lifecycle.</summary>
public record SetClauseStatusRequest(ClauseStatus Status);

/// <summary>Identifier of the newly created clause.</summary>
public record CreateClauseResponse(Guid Id);
