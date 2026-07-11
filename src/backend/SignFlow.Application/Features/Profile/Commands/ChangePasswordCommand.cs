using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Profile.Commands;

public record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.")
            .NotEqual(x => x.CurrentPassword).WithMessage("New password must be different from the current password.");
    }
}

public class ChangePasswordCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUser,
    ILogger<ChangePasswordCommandHandler> logger) : ICommandHandler<ChangePasswordCommand>
{
    public async Task HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        var result = await userManager.ChangePasswordAsync(user, command.CurrentPassword, command.NewPassword);
        if (!result.Succeeded)
            throw new Common.Exceptions.ValidationException(result.Errors.Select(e => new FluentValidation.Results.ValidationFailure("", e.Description)));

        logger.LogInformation("User {UserId} changed their password", userId);
    }
}
