using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Auth.Commands;

public record LoginCommand(string Email, string Password, bool RememberMe = false) : ICommand<LoginResult>;

public record LoginResult(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserInfo User);

public record UserInfo(Guid Id, string Email, string FirstName, string LastName, string FullName, IList<string> Roles, string? AvatarUrl, bool MfaEnabled, string? Department, string? Company = null);

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
    }
}

public class LoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    ILogger<LoginCommandHandler> logger) : ICommandHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(command.Email)
            ?? throw new UnauthorizedAccessException("Invalid email or password.");

        if (!user.IsActive)
            throw new ForbiddenAccessException("Account is deactivated.");

        var passwordValid = await userManager.CheckPasswordAsync(user, command.Password);
        if (!passwordValid)
        {
            await userManager.AccessFailedAsync(user);
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        if (await userManager.IsLockedOutAsync(user))
            throw new ForbiddenAccessException("Account is temporarily locked. Please try again later.");

        await userManager.ResetAccessFailedCountAsync(user);

        var roles = await userManager.GetRolesAsync(user);
        var accessToken = tokenService.GenerateAccessToken(user, roles);
        var refreshToken = tokenService.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(command.RememberMe ? 30 : 7);
        user.LastLoginAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        logger.LogInformation("User {Email} logged in successfully", command.Email);

        return new LoginResult(
            accessToken,
            refreshToken,
            DateTime.UtcNow.AddMinutes(60),
            new UserInfo(user.Id, user.Email!, user.FirstName, user.LastName, user.FullName, roles, user.AvatarUrl, user.MfaEnabled, user.Department, user.Company));
    }
}
