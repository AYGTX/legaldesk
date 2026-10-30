using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Auth.Commands;
using SignFlow.Application.Features.Profile;
using SignFlow.Application.Features.Profile.Commands;
using SignFlow.Application.Features.Profile.Queries;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Self-service profile endpoints for the currently authenticated user</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/profile")]
[ApiController]
[Authorize]
public class ProfileController(
    IQueryHandler<GetMyProfileQuery, ProfileDto> getProfileHandler,
    ICommandHandler<UpdateProfileCommand, ProfileDto> updateProfileHandler,
    ICommandHandler<ChangePasswordCommand> changePasswordHandler,
    ICommandHandler<UpdateAvatarCommand, string> updateAvatarHandler,
    ICommandHandler<BeginMfaSetupCommand, MfaSetupDto> beginMfaHandler,
    ICommandHandler<ConfirmMfaSetupCommand, IReadOnlyList<string>> confirmMfaHandler,
    ICommandHandler<DisableMfaCommand> disableMfaHandler,
    IQueryHandler<GetMySessionsQuery, List<SessionDto>> sessionsHandler,
    ICommandHandler<EndMySessionCommand> endSessionHandler) : ControllerBase
{
    /// <summary>Start two-factor setup: a new authenticator key and the otpauth:// address for the QR code</summary>
    [HttpPost("mfa/setup")]
    [ProducesResponseType(typeof(MfaSetupDto), 200)]
    public async Task<ActionResult<MfaSetupDto>> BeginMfaSetup(CancellationToken ct) =>
        Ok(await beginMfaHandler.HandleAsync(new BeginMfaSetupCommand(), ct));

    /// <summary>Confirm with a code from the app; returns 10 recovery codes, shown only this once. Other sessions end.</summary>
    [HttpPost("mfa/confirm")]
    [ProducesResponseType(typeof(RecoveryCodesResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<RecoveryCodesResponse>> ConfirmMfa([FromBody] MfaCodeRequest request, CancellationToken ct) =>
        Ok(new RecoveryCodesResponse(await confirmMfaHandler.HandleAsync(new ConfirmMfaSetupCommand(request.Code), ct)));

    /// <summary>Turn two-factor off (refused for roles that require it)</summary>
    [HttpPost("mfa/disable")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> DisableMfa([FromBody] MfaCodeRequest request, CancellationToken ct)
    {
        await disableMfaHandler.HandleAsync(new DisableMfaCommand(request.Code), ct);
        return NoContent();
    }

    /// <summary>My open sessions, most recently used first</summary>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(List<SessionDto>), 200)]
    public async Task<ActionResult<List<SessionDto>>> GetSessions(CancellationToken ct) =>
        Ok(await sessionsHandler.HandleAsync(new GetMySessionsQuery(), ct));

    /// <summary>End one of my sessions</summary>
    [HttpDelete("sessions/{id:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> EndSession([FromRoute] Guid id, CancellationToken ct)
    {
        await endSessionHandler.HandleAsync(new EndMySessionCommand(id), ct);
        return NoContent();
    }

    /// <summary>Get the current user's profile</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ProfileDto), 200)]
    public async Task<ActionResult<ProfileDto>> GetMyProfile(CancellationToken ct)
    {
        var result = await getProfileHandler.HandleAsync(new GetMyProfileQuery(), ct);
        return Ok(result);
    }

    /// <summary>Update the current user's profile</summary>
    [HttpPut]
    [ProducesResponseType(typeof(ProfileDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<ProfileDto>> UpdateMyProfile([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var result = await updateProfileHandler.HandleAsync(
            new UpdateProfileCommand(request.FirstName, request.LastName, request.Company, request.Department, request.Title), ct);
        return Ok(result);
    }

    /// <summary>Change the current user's password</summary>
    [HttpPost("change-password")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        await changePasswordHandler.HandleAsync(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword), ct);
        return NoContent();
    }

    /// <summary>Upload a new avatar image</summary>
    [HttpPost("avatar")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [ProducesResponseType(typeof(AvatarResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<AvatarResponse>> UploadAvatar([FromForm] UploadAvatarRequest request, CancellationToken ct)
    {
        if (request.File.Length == 0)
            return BadRequest(new ProblemDetails { Title = "Invalid file", Detail = "File is empty." });

        var url = await updateAvatarHandler.HandleAsync(
            new UpdateAvatarCommand(request.File.OpenReadStream(), request.File.FileName, request.File.ContentType), ct);
        return Ok(new AvatarResponse(url));
    }
}

public record UpdateProfileRequest(string FirstName, string LastName, string? Company, string? Department, string? Title);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record UploadAvatarRequest(IFormFile File);
public record AvatarResponse(string AvatarUrl);
/// <summary>A code from the authenticator app</summary>
public record MfaCodeRequest(string Code);
/// <summary>Recovery codes, each usable once</summary>
public record RecoveryCodesResponse(IReadOnlyList<string> Codes);
