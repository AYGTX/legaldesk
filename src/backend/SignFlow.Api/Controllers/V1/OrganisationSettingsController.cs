using SignFlow.Api.Filters;
using SignFlow.Application.Common.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Administration;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// The organisation's own settings: whether contract text may be sent to the Harvey assistant's model (off by
/// default, D3), and the days before a deadline its alerts go out (R-M10-3).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organisation-settings")]
[ApiController]
[Authorize]
[RequirePermission(Permissions.ManageReviewSettings)]
public class OrganisationSettingsController(
    IQueryHandler<GetOrganisationSettingsQuery, OrganisationSettingsDto> getHandler,
    ICommandHandler<SetOrganisationSettingsCommand, OrganisationSettingsDto> setHandler) : ControllerBase
{
    /// <summary>The organisation's settings</summary>
    [HttpGet]
    [ProducesResponseType(typeof(OrganisationSettingsDto), 200)]
    public async Task<ActionResult<OrganisationSettingsDto>> Get(CancellationToken ct)
        => Ok(await getHandler.HandleAsync(new GetOrganisationSettingsQuery(), ct));

    /// <summary>Turn the assistant on or off, and set the alert days</summary>
    [HttpPut]
    [ProducesResponseType(typeof(OrganisationSettingsDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<OrganisationSettingsDto>> Set([FromBody] SetOrganisationSettingsCommand request, CancellationToken ct)
        => Ok(await setHandler.HandleAsync(request, ct));
}

/// <summary>Whether the Harvey assistant is available to the signed-in person's organisation.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/assistant")]
[ApiController]
[Authorize]
public class AssistantController(IQueryHandler<GetAssistantStatusQuery, AssistantStatusDto> statusHandler) : ControllerBase
{
    /// <summary>On or off, and which model</summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(AssistantStatusDto), 200)]
    public async Task<ActionResult<AssistantStatusDto>> Status(CancellationToken ct) => Ok(await statusHandler.HandleAsync(new GetAssistantStatusQuery(), ct));
}
