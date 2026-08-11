using FluentValidation;
using Microsoft.AspNetCore.Identity;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Auth.Commands;

public record RefreshTokenCommand(string AccessToken, string RefreshToken) : ICommand<LoginResult>;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.AccessToken).NotEmpty();
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public class RefreshTokenCommandHandler(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService) : ICommandHandler<RefreshTokenCommand, LoginResult>
{
    public async Task<LoginResult> HandleAsync(RefreshTokenCommand command, CancellationToken cancellationToken = default)
    {
        // The FluentValidation NotEmpty() rules above are registered but never invoked (no
        // pipeline/decorator calls IValidator<T> anywhere in this codebase), so guard here too —
        // an empty AccessToken reaching JwtSecurityTokenHandler.ValidateToken throws an unhandled
        // ArgumentNullException (IDX10000) instead of a clean 401.
        if (string.IsNullOrWhiteSpace(command.AccessToken) || string.IsNullOrWhiteSpace(command.RefreshToken))
            throw new UnauthorizedAccessException("Invalid token.");

        var principal = tokenService.GetPrincipalFromExpiredToken(command.AccessToken);
        var email = principal.Identity?.Name ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? throw new UnauthorizedAccessException("Invalid token.");

        var user = await userManager.FindByEmailAsync(email)
            ?? throw new UnauthorizedAccessException("User not found.");

        if (user.RefreshToken != command.RefreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        var roles = await userManager.GetRolesAsync(user);
        var newAccessToken = tokenService.GenerateAccessToken(user, roles);
        var newRefreshToken = tokenService.GenerateRefreshToken();

        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        await userManager.UpdateAsync(user);

        return new LoginResult(
            newAccessToken,
            newRefreshToken,
            DateTime.UtcNow.AddMinutes(60),
            new UserInfo(user.Id, user.Email!, user.FirstName, user.LastName, user.FullName, roles, user.AvatarUrl, user.MfaEnabled, user.Department));
    }
}
