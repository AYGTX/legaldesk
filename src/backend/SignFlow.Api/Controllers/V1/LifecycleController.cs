using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Lifecycle;
using SignFlow.Domain.Enums;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// A signed contract's life (M10): its term, the decision on it, its obligations and what happened to it. The dates are
/// entered by people; nothing here is filled in by the model (R-M12-9).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/documents/{id:guid}")]
[ApiController]
[Authorize]
public class LifecycleController : ControllerBase
{
    /// <summary>The term, its computed deadlines, the obligations and the term's history</summary>
    [HttpGet("lifecycle")]
    [ProducesResponseType(typeof(ContractLifecycleDetailDto), 200)]
    public async Task<ActionResult<ContractLifecycleDetailDto>> Get([FromRoute] Guid id,
        [FromServices] IQueryHandler<GetContractLifecycleDetailQuery, ContractLifecycleDetailDto> handler, CancellationToken ct)
        => Ok(await handler.HandleAsync(new GetContractLifecycleDetailQuery(id), ct));

    /// <summary>Record the term a person checked against the contract; re-arms the alerts</summary>
    [HttpPut("term")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> SetTerm([FromRoute] Guid id, [FromBody] SetTermRequest request,
        [FromServices] ICommandHandler<SetContractTermCommand> handler, CancellationToken ct)
    {
        await handler.HandleAsync(new SetContractTermCommand(id, request.StartDate, request.EndDate, request.RenewalMode,
            request.RenewalMonths, request.NoticeAmount, request.NoticeUnit, request.NoticeForm), ct);
        return NoContent();
    }

    /// <summary>Record the decision on the term (renew, renegotiate, terminate with proof, let expire); stops the alerts</summary>
    [HttpPost("term/decision")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> Decide([FromRoute] Guid id, [FromForm] DecideTermRequest request,
        [FromServices] ICommandHandler<DecideTermCommand> handler, CancellationToken ct)
    {
        await using var proof = request.Proof?.OpenReadStream();
        await handler.HandleAsync(new DecideTermCommand(id, request.Decision, request.NoticeSentAt, proof,
            request.Proof?.FileName, request.Proof?.ContentType), ct);
        return NoContent();
    }

    /// <summary>Add an obligation; one taken from Harvey's key data carries its quote and waits for confirmation</summary>
    [HttpPost("obligations")]
    [ProducesResponseType(typeof(IdResponse), 200)]
    public async Task<ActionResult<IdResponse>> AddObligation([FromRoute] Guid id, [FromBody] ObligationRequest request,
        [FromServices] ICommandHandler<AddObligationCommand, Guid> handler, CancellationToken ct)
        => Ok(new IdResponse(await handler.HandleAsync(new AddObligationCommand(id, request.Title, request.Kind, request.DueDate,
            request.Recurrence, request.OwnerId, request.Quote, request.Source ?? ObligationSource.Person), ct)));

    [HttpPut("obligations/{obligationId:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> UpdateObligation([FromRoute] Guid id, [FromRoute] Guid obligationId, [FromBody] ObligationRequest request,
        [FromServices] ICommandHandler<UpdateObligationCommand> handler, CancellationToken ct)
    {
        await handler.HandleAsync(new UpdateObligationCommand(id, obligationId, request.Title, request.Kind, request.DueDate,
            request.Recurrence, request.OwnerId, request.Quote), ct);
        return NoContent();
    }

    /// <summary>Confirm, mark done (a recurring one moves to its next date), waive or delete an obligation</summary>
    [HttpPost("obligations/{obligationId:guid}/{action}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Act([FromRoute] Guid id, [FromRoute] Guid obligationId, [FromRoute] ObligationAction action,
        [FromServices] ICommandHandler<ActOnObligationCommand> handler, CancellationToken ct)
    {
        await handler.HandleAsync(new ActOnObligationCommand(id, obligationId, action), ct);
        return NoContent();
    }
}

public record SetTermRequest(DateTime? StartDate, DateTime? EndDate, RenewalMode RenewalMode, int? RenewalMonths,
    int? NoticeAmount, NoticeUnit NoticeUnit, NoticeForm? NoticeForm);

public class DecideTermRequest
{
    public TermDecision Decision { get; set; }
    public DateTime? NoticeSentAt { get; set; }
    public IFormFile? Proof { get; set; }
}

public record ObligationRequest(string Title, ObligationKind Kind, DateTime? DueDate, ObligationRecurrence Recurrence,
    Guid? OwnerId, string? Quote, ObligationSource? Source = null);

public record IdResponse(Guid Id);
