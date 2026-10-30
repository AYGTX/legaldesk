using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Auth.Commands;

public record LoginCommand(string Email, string Password, ClientInfo Client) : ICommand<LoginOutcome>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

/// <summary>
/// Password sign-in (R-M1-5). Unknown e-mail, wrong password, deactivated and locked accounts all get the same
/// answer, after the same password-hash work, so the response tells an attacker nothing about who has an account.
/// </summary>
public class LoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    AuthSessions sessions,
    ITokenService tokens,
    ILogger<LoginCommandHandler> logger) : ICommandHandler<LoginCommand, LoginOutcome>
{
    public const string InvalidCredentials = "Invalid email or password.";

    // Verifying against this hash costs what a real check costs, so an unknown e-mail answers as slowly as a known one.
    private static readonly Lazy<string> DummyHash =
        new(() => new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), Guid.NewGuid().ToString()));

    public async Task<LoginOutcome> HandleAsync(LoginCommand command, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(command.Email);
        if (user is null)
        {
            userManager.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), DummyHash.Value, command.Password);
            logger.LogInformation("Sign-in refused: unknown e-mail");
            throw new UnauthorizedAccessException(InvalidCredentials);
        }

        var locked = await userManager.IsLockedOutAsync(user);
        var passwordOk = await userManager.CheckPasswordAsync(user, command.Password);

        if (locked || !user.IsActive || !passwordOk)
        {
            if (!locked && !passwordOk)
                await userManager.AccessFailedAsync(user);
            logger.LogInformation("Sign-in refused for user {UserId}: {Reason}", user.Id,
                locked ? "locked" : !user.IsActive ? "deactivated" : "wrong password");
            throw new UnauthorizedAccessException(InvalidCredentials);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        if (user.TwoFactorEnabled)
            return LoginOutcome.NeedsMfa(tokens.GenerateMfaChallenge(user));

        logger.LogInformation("User {UserId} signed in", user.Id);
        return LoginOutcome.Signed(await sessions.StartAsync(user, command.Client, cancellationToken));
    }
}
