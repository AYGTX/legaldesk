using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Auth;
using SignFlow.Application.Features.Auth.Commands;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// Sign-in and sessions (R-M1-4). The access token is returned in the body and kept in memory by the web app; the
/// refresh token travels only in an HttpOnly, Secure, SameSite=Strict cookie that scripts cannot read.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimits.Auth)]
public class AuthController(
    ICommandHandler<LoginCommand, LoginOutcome> loginHandler,
    ICommandHandler<MfaLoginCommand, AuthSession> mfaLoginHandler,
    ICommandHandler<RefreshTokenCommand, AuthSession> refreshHandler,
    ICommandHandler<LogoutCommand> logoutHandler,
    ICommandHandler<AcceptInvitationCommand, AuthSession> acceptInvitationHandler,
    ICommandHandler<ForgotPasswordCommand> forgotHandler,
    ICommandHandler<ResetPasswordCommand> resetHandler) : ControllerBase
{
    public const string SessionCookie = "ld_session";

    /// <summary>Sign in with e-mail and password; answers with a session, or with a two-factor challenge</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(SignInResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    public async Task<ActionResult<SignInResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var outcome = await loginHandler.HandleAsync(new LoginCommand(request.Email, request.Password, Client()), ct);
        return outcome.Session is { } session
            ? Ok(Issue(session))
            : Ok(new SignInResponse(null, null, null, MfaRequired: true, MfaChallenge: outcome.MfaChallenge));
    }

    /// <summary>Second step: the challenge from login plus a code from the authenticator app or a recovery code</summary>
    [HttpPost("mfa")]
    [ProducesResponseType(typeof(SignInResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    public async Task<ActionResult<SignInResponse>> Mfa([FromBody] MfaRequest request, CancellationToken ct) =>
        Ok(Issue(await mfaLoginHandler.HandleAsync(new MfaLoginCommand(request.Challenge, request.Code, Client()), ct)));

    /// <summary>A new access token from the session cookie; the cookie itself is rotated</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(SignInResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    public async Task<ActionResult<SignInResponse>> Refresh(CancellationToken ct)
    {
        try
        {
            return Ok(Issue(await refreshHandler.HandleAsync(
                new RefreshTokenCommand(Request.Cookies[SessionCookie], Client()), ct)));
        }
        catch (UnauthorizedAccessException)
        {
            ClearCookie();
            throw;
        }
    }

    /// <summary>End this device's session</summary>
    [HttpPost("logout")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await logoutHandler.HandleAsync(new LogoutCommand(Request.Cookies[SessionCookie]), ct);
        ClearCookie();
        return NoContent();
    }

    /// <summary>Accept an invitation: set a password and sign in</summary>
    [HttpPost("invitations/accept")]
    [ProducesResponseType(typeof(SignInResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<SignInResponse>> AcceptInvitation([FromBody] AcceptInvitationRequest request, CancellationToken ct) =>
        Ok(Issue(await acceptInvitationHandler.HandleAsync(new AcceptInvitationCommand(request.Token, request.Password, Client()), ct)));

    /// <summary>Request a password reset e-mail (same answer whether or not the address has an account)</summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordCommand command, CancellationToken ct)
    {
        await forgotHandler.HandleAsync(command, ct);
        return NoContent();
    }

    /// <summary>Set a new password with the link's token; every session of the account ends</summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command, CancellationToken ct)
    {
        await resetHandler.HandleAsync(command, ct);
        return NoContent();
    }

    private ClientInfo Client() =>
        new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());

    private SignInResponse Issue(AuthSession session)
    {
        Response.Cookies.Append(SessionCookie, session.RefreshToken, CookieOptions(session.SessionExpiresAt));
        return new SignInResponse(session.AccessToken, session.AccessTokenExpiresAt, session.User, MfaRequired: false, MfaChallenge: null);
    }

    private void ClearCookie() => Response.Cookies.Delete(SessionCookie, CookieOptions(null));

    private static CookieOptions CookieOptions(DateTime? expires) => new()
    {
        HttpOnly = true,
        Secure = true, // browsers accept Secure cookies on http://localhost, so this holds in development too
        SameSite = SameSiteMode.Strict,
        Path = "/api/v1/auth",
        Expires = expires
    };
}

/// <summary>E-mail and password</summary>
public record LoginRequest(string Email, string Password);

/// <summary>Challenge from the login answer and a TOTP or recovery code</summary>
public record MfaRequest(string Challenge, string Code);

/// <summary>Invitation token from the e-mailed link and the new password</summary>
public record AcceptInvitationRequest(string Token, string Password);

/// <summary>Either a session (access token, its expiry, the user) or a two-factor challenge to answer</summary>
public record SignInResponse(string? AccessToken, DateTime? ExpiresAt, UserInfo? User, bool MfaRequired, string? MfaChallenge);
