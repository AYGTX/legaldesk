using SignFlow.Api.Filters;
using SignFlow.Application.Common.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Reviews;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// The organisation's choices about Harvey's grid (R-M0-7): which rules in service it reviews with, and at which
/// severity. Applies to the next review of each contract.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/review-settings")]
[ApiController]
[Authorize]
[RequirePermission(Permissions.ManageReviewSettings)]
public class ReviewSettingsController(
    IQueryHandler<GetReviewSettingsQuery, List<ReviewRuleSettingDto>> getHandler,
    ICommandHandler<SetReviewSettingCommand> setHandler) : ControllerBase
{
    /// <summary>The rules in service and the organisation's setting for each</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ReviewRuleSettingDto>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public async Task<ActionResult<List<ReviewRuleSettingDto>>> Get(CancellationToken ct)
        => Ok(await getHandler.HandleAsync(new GetReviewSettingsQuery(), ct));

    /// <summary>Switch a rule on or off, or change its severity (null keeps the rule's own)</summary>
    [HttpPut("{ruleKey}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> Set([FromRoute] string ruleKey, [FromBody] SetReviewSettingRequest request, CancellationToken ct)
    {
        await setHandler.HandleAsync(new SetReviewSettingCommand(ruleKey, request.Enabled, request.SeverityOverride), ct);
        return NoContent();
    }
}

public record SetReviewSettingRequest(bool Enabled, string? SeverityOverride);
