using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Auth.Commands;

public record RegisterCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? Company = null,
    string? Title = null) : ICommand<LoginResult>;

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");
    }
}

public class RegisterCommandHandler(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    ILogger<RegisterCommandHandler> logger) : ICommandHandler<RegisterCommand, LoginResult>
{
    public async Task<LoginResult> HandleAsync(RegisterCommand command, CancellationToken cancellationToken = default)
    {
        var existingUser = await userManager.FindByEmailAsync(command.Email);
        if (existingUser is not null)
            throw new ConflictException($"An account with email '{command.Email}' already exists.");

        var user = new ApplicationUser
        {
            UserName = command.Email,
            Email = command.Email,
            FirstName = command.FirstName,
            LastName = command.LastName,
            Company = command.Company,
            Title = command.Title,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, command.Password);
        if (!result.Succeeded)
            throw new Common.Exceptions.ValidationException(result.Errors.Select(e => new FluentValidation.Results.ValidationFailure("", e.Description)));

        await userManager.AddToRoleAsync(user, "Employee");

        var roles = await userManager.GetRolesAsync(user);
        var accessToken = tokenService.GenerateAccessToken(user, roles);
        var refreshToken = tokenService.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        await userManager.UpdateAsync(user);

        logger.LogInformation("New user registered: {Email}", command.Email);

        return new LoginResult(
            accessToken,
            refreshToken,
            DateTime.UtcNow.AddMinutes(60),
            new UserInfo(user.Id, user.Email!, user.FirstName, user.LastName, user.FullName, roles, null, false, user.Department, user.Company));
    }
}
