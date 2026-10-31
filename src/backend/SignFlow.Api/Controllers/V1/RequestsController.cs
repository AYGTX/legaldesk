using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Requests.Queries;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Personal request tracking — workflows the user sent and requests sent to them</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/requests")]
[ApiController]
[Authorize]
public class RequestsController(
    IQueryHandler<GetMyRequestsQuery, MyRequestsDto> getMyRequestsHandler,
    SignFlow.Application.Features.Signing.ISigningAccess signingAccess) : ControllerBase
{
    /// <summary>
    /// Opens a signing session for a request addressed to the signed-in user: their account proves the e-mail address,
    /// so no code is needed (R-M5-3). The session secret goes in the X-Signing-Session header on /sign/session/...
    /// </summary>
    [HttpPost("{recipientId:guid}/signing-session")]
    [ProducesResponseType(typeof(SignFlow.Application.Features.Signing.OpenedSigningSession), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<ActionResult<SignFlow.Application.Features.Signing.OpenedSigningSession>> OpenSigningSession(
        [FromRoute] Guid recipientId, CancellationToken ct)
        => Ok(await signingAccess.OpenForAccountAsync(recipientId, HttpContext.Connection.RemoteIpAddress?.ToString(), ct));

    /// <summary>Get the current user's sent workflows and received signing requests</summary>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(MyRequestsDto), 200)]
    public async Task<ActionResult<MyRequestsDto>> GetMyRequests(CancellationToken ct)
    {
        var result = await getMyRequestsHandler.HandleAsync(new GetMyRequestsQuery(), ct);
        return Ok(result);
    }
}
