using FluentValidation;
using Microsoft.AspNetCore.Identity;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Auth.Commands;

public record ResetPasswordCommand(string Email, string Token, string NewPassword) : ICommand;

public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");
    }
}

public class ResetPasswordCommandHandler(UserManager<ApplicationUser> userManager, AuthSessions sessions)
    : ICommandHandler<ResetPasswordCommand>
{
    public const string InvalidLink = "This reset link is invalid or has expired. Ask for a new one.";

    public async Task HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken = default)
    {
        // Same answer for an unknown address as for a bad token: the link alone must not reveal who has an account.
        var user = await userManager.FindByEmailAsync(command.Email);
        if (user is null || !user.IsActive)
            throw new Common.Exceptions.ValidationException([new FluentValidation.Results.ValidationFailure("token", InvalidLink)]);

        var result = await userManager.ResetPasswordAsync(user, command.Token, command.NewPassword);
        if (!result.Succeeded)
            throw new Common.Exceptions.ValidationException(result.Errors.Select(e => new FluentValidation.Results.ValidationFailure("Password", e.Description)));

        // Whoever knew the old password is signed out everywhere (R-M1-6).
        await sessions.EndAllAsync(user.Id, "password-reset", cancellationToken);
    }
}
