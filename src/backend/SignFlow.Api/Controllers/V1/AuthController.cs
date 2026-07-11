using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Auth.Commands;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Authentication endpoints</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
public class AuthController(
    ICommandHandler<LoginCommand, LoginResult> loginHandler,
    ICommandHandler<RegisterCommand, LoginResult> registerHandler,
    ICommandHandler<RefreshTokenCommand, LoginResult> refreshHandler,
    ICommandHandler<ForgotPasswordCommand> forgotHandler,
    ICommandHandler<ResetPasswordCommand> resetHandler) : ControllerBase
{
    /// <summary>Login with email and password</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    public async Task<ActionResult<LoginResult>> Login([FromBody] LoginCommand command, CancellationToken ct)
    {
        var result = await loginHandler.HandleAsync(command, ct);
        return Ok(result);
    }

    /// <summary>Register a new account</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(LoginResult), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<LoginResult>> Register([FromBody] RegisterCommand command, CancellationToken ct)
    {
        var result = await registerHandler.HandleAsync(command, ct);
        return StatusCode(201, result);
    }

    /// <summary>Refresh access token using refresh token</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(LoginResult), 200)]
    public async Task<ActionResult<LoginResult>> Refresh([FromBody] RefreshTokenCommand command, CancellationToken ct)
    {
        var result = await refreshHandler.HandleAsync(command, ct);
        return Ok(result);
    }

    /// <summary>Request a password reset email</summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordCommand command, CancellationToken ct)
    {
        await forgotHandler.HandleAsync(command, ct);
        return NoContent();
    }

    /// <summary>Reset password using token</summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command, CancellationToken ct)
    {
        await resetHandler.HandleAsync(command, ct);
        return NoContent();
    }
}
