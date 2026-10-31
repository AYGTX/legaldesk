using SignFlow.Api.Filters;
using SignFlow.Application.Common.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Clauses;
using SignFlow.Application.Features.Clauses.Commands;
using SignFlow.Domain.Enums;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// The clause library (M9): each clause keeps every wording it has had; an approved wording never changes, a new one is
/// drafted and approved by someone other than its author. Curation is for administrators and managers; approved
/// wordings are readable by anyone drafting or reviewing a contract.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clauses")]
[ApiController]
[Authorize]
public class ClausesController(
    IQueryHandler<GetClausesQuery, List<ClauseDto>> getAllHandler,
    IQueryHandler<GetApprovedClausesQuery, List<ApprovedClauseDto>> getApprovedHandler,
    IQueryHandler<GetClausesForRuleQuery, List<ApprovedClauseDto>> forRuleHandler,
    ICommandHandler<CreateClauseCommand, Guid> createHandler,
    ICommandHandler<DescribeClauseCommand> describeHandler,
    ICommandHandler<RedraftClauseCommand, int> redraftHandler,
    ICommandHandler<ApproveClauseVersionCommand> approveHandler,
    ICommandHandler<DeprecateClauseVersionCommand> deprecateHandler,
    ICommandHandler<LinkClauseRulesCommand> rulesHandler,
    ICommandHandler<DeleteClauseCommand> deleteHandler,
    ICommandHandler<RecordClauseUsageCommand> recordUsageHandler) : ControllerBase
{
    /// <summary>Every clause with all its wordings, drafts included (administrators and managers)</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ClauseDto>), 200)]
    public async Task<ActionResult<List<ClauseDto>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] ClauseCategory? category = null,
        [FromQuery] string? language = null,
        [FromQuery] ClauseStatus? status = null,
        CancellationToken ct = default)
        => Ok(await getAllHandler.HandleAsync(new GetClausesQuery(search, category, language, status), ct));

    /// <summary>Approved wordings available for insertion into a contract</summary>
    [HttpGet("approved")]
    [ProducesResponseType(typeof(List<ApprovedClauseDto>), 200)]
    public async Task<ActionResult<List<ApprovedClauseDto>>> GetApproved(
        [FromQuery] string? search = null,
        [FromQuery] ClauseCategory? category = null,
        [FromQuery] string? language = null,
        [FromQuery] Guid? requestTypeId = null,
        CancellationToken ct = default)
        => Ok(await getApprovedHandler.HandleAsync(new GetApprovedClausesQuery(search, category, language, requestTypeId), ct));

    /// <summary>The organisation's approved wordings for a Harvey rule, in the given language first</summary>
    [HttpGet("for-rule/{ruleKey}")]
    [ProducesResponseType(typeof(List<ApprovedClauseDto>), 200)]
    public async Task<ActionResult<List<ApprovedClauseDto>>> ForRule([FromRoute] string ruleKey, [FromQuery] string? language = null, CancellationToken ct = default)
        => Ok(await forRuleHandler.HandleAsync(new GetClausesForRuleQuery(ruleKey, language), ct));

    /// <summary>Add a clause with its first wording, as a draft; from a finding, linked to its rule</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateClauseResponse), 201)]
    public async Task<ActionResult<CreateClauseResponse>> Create([FromBody] CreateClauseRequest request, CancellationToken ct)
    {
        var id = await createHandler.HandleAsync(
            new CreateClauseCommand(request.Title, request.Category, request.Body, request.Language,
                request.Guidance, request.RiskLevel, request.RequestTypeId, request.RuleKey), ct);
        return CreatedAtAction(nameof(GetAll), new { }, new CreateClauseResponse(id));
    }

    /// <summary>Change what describes a clause (title, category, guidance, risk, family); not its wording</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Describe([FromRoute] Guid id, [FromBody] DescribeClauseRequest request, CancellationToken ct)
    {
        await describeHandler.HandleAsync(
            new DescribeClauseCommand(id, request.Title, request.Category, request.Guidance, request.RiskLevel, request.RequestTypeId), ct);
        return NoContent();
    }

    /// <summary>New wording: rewrites the open draft, or drafts the next version (the approved one stays in use)</summary>
    [HttpPost("{id:guid}/versions")]
    [ProducesResponseType(typeof(ClauseVersionResponse), 200)]
    public async Task<ActionResult<ClauseVersionResponse>> Redraft([FromRoute] Guid id, [FromBody] RedraftClauseRequest request, CancellationToken ct)
        => Ok(new ClauseVersionResponse(await redraftHandler.HandleAsync(new RedraftClauseCommand(id, request.Body, request.Language), ct)));

    /// <summary>Approve a draft wording (a manager or administrator other than its author)</summary>
    [HttpPost("{id:guid}/versions/{number:int}/approve")]
    [RequirePermission(Permissions.ApproveClauses)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Approve([FromRoute] Guid id, [FromRoute] int number, CancellationToken ct)
    {
        await approveHandler.HandleAsync(new ApproveClauseVersionCommand(id, number), ct);
        return NoContent();
    }

    /// <summary>Withdraw a wording: still readable and cited, never offered again</summary>
    [HttpPost("{id:guid}/versions/{number:int}/deprecate")]
    [RequirePermission(Permissions.ApproveClauses)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Deprecate([FromRoute] Guid id, [FromRoute] int number, CancellationToken ct)
    {
        await deprecateHandler.HandleAsync(new DeprecateClauseVersionCommand(id, number), ct);
        return NoContent();
    }

    /// <summary>The Harvey rules this clause answers</summary>
    [HttpPut("{id:guid}/rules")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> LinkRules([FromRoute] Guid id, [FromBody] LinkRulesRequest request, CancellationToken ct)
    {
        await rulesHandler.HandleAsync(new LinkClauseRulesCommand(id, request.RuleKeys ?? []), ct);
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

    /// <summary>Delete a clause that has never been used (administrators and managers)</summary>
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
    ClauseRiskLevel? RiskLevel,
    Guid? RequestTypeId,
    string? RuleKey = null);

/// <summary>What describes a clause.</summary>
public record DescribeClauseRequest(
    string Title,
    ClauseCategory Category,
    string? Guidance,
    ClauseRiskLevel? RiskLevel,
    Guid? RequestTypeId);

/// <summary>New wording for a clause.</summary>
public record RedraftClauseRequest(string Body, string Language);

/// <summary>The Harvey rule keys a clause answers.</summary>
public record LinkRulesRequest(List<string>? RuleKeys);

/// <summary>Identifier of the newly created clause.</summary>
public record CreateClauseResponse(Guid Id);

/// <summary>The number of the wording just written.</summary>
public record ClauseVersionResponse(int Number);
