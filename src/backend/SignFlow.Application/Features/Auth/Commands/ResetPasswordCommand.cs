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

public class ResetPasswordCommandHandler(UserManager<ApplicationUser> userManager) : ICommandHandler<ResetPasswordCommand>
{
    public async Task HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(command.Email)
            ?? throw new NotFoundException("User not found.");

        var result = await userManager.ResetPasswordAsync(user, command.Token, command.NewPassword);
        if (!result.Succeeded)
            throw new Common.Exceptions.ValidationException(result.Errors.Select(e => new FluentValidation.Results.ValidationFailure("Password", e.Description)));

        user.RefreshToken = null;
        user.RefreshTokenExpiryTime = null;
        await userManager.UpdateAsync(user);
    }
}
