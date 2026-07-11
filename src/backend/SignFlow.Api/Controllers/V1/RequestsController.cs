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
    IQueryHandler<GetMyRequestsQuery, MyRequestsDto> getMyRequestsHandler) : ControllerBase
{
    /// <summary>Get the current user's sent workflows and received signing requests</summary>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(MyRequestsDto), 200)]
    public async Task<ActionResult<MyRequestsDto>> GetMyRequests(CancellationToken ct)
    {
        var result = await getMyRequestsHandler.HandleAsync(new GetMyRequestsQuery(), ct);
        return Ok(result);
    }
}
