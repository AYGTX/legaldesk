using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Gdpr.Commands;
using SignFlow.Application.Features.Gdpr.Queries;
using SignFlow.Domain.Enums;

namespace SignFlow.Api.Controllers.V1;

/// <summary>GDPR data-subject rights and consent management</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
public class GdprController(
    ICommandHandler<RecordConsentCommand> consentHandler,
    ICommandHandler<SubmitDsarCommand, Guid> dsarHandler,
    IQueryHandler<ExportUserDataQuery, UserDataExportDto> exportHandler) : ControllerBase
{
    /// <summary>Record user consent for a specific tier (functional / analytics)</summary>
    [HttpPost("consent")]
    [Authorize]
    [ProducesResponseType(204)]
    public async Task<IActionResult> RecordConsent([FromBody] RecordConsentRequest request, CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value ?? string.Empty;

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        await consentHandler.HandleAsync(
            new RecordConsentCommand(userId, request.Tier, request.Granted, request.Source, ip, ua), ct);
        return NoContent();
    }

    /// <summary>Submit a Data Subject Access Request (Art. 15-21 GDPR)</summary>
    [HttpPost("dsar")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(DsarSubmittedResponse), 201)]
    public async Task<IActionResult> SubmitDsar([FromBody] SubmitDsarRequest request, CancellationToken ct)
    {
        var id = await dsarHandler.HandleAsync(
            new SubmitDsarCommand(request.Email, request.Name, request.RequestType, request.Details), ct);
        return StatusCode(201, new DsarSubmittedResponse(id, DateTime.UtcNow.AddDays(30)));
    }

    /// <summary>Export all personal data for the current authenticated user (Art. 15 / Art. 20)</summary>
    [HttpGet("export")]
    [Authorize]
    [ProducesResponseType(typeof(UserDataExportDto), 200)]
    public async Task<ActionResult<UserDataExportDto>> ExportMyData(CancellationToken ct)
    {
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? User.FindFirst("email")?.Value ?? string.Empty;

        var data = await exportHandler.HandleAsync(new ExportUserDataQuery(email), ct);
        return Ok(data);
    }
}

public record RecordConsentRequest(ConsentTier Tier, bool Granted, string Source);
public record SubmitDsarRequest(string Email, string? Name, DsarType RequestType, string? Details);
public record DsarSubmittedResponse(Guid RequestId, DateTime DueBy);
