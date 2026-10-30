using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Auth;
using SignFlow.Application.Features.Auth.Commands;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Administration.Commands;

/// <summary>
/// An Administrator resets someone's password: their sessions end and they receive a reset link. The Administrator
/// never chooses or sees the new password (R-M1-6).
/// </summary>
public record ResetUserPasswordCommand(Guid UserId) : ICommand;

public class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public class ResetUserPasswordCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUser,
    AuthSessions sessions,
    PasswordResetMail resetMail,
    ILogger<ResetUserPasswordCommandHandler> logger) : ICommandHandler<ResetUserPasswordCommand>
{
    public async Task HandleAsync(ResetUserPasswordCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageUsers))
            throw new ForbiddenAccessException();

        var user = await userManager.FindByIdAsync(command.UserId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), command.UserId);

        // The old password stops working at once, not when the person gets round to the e-mail.
        await userManager.UpdateSecurityStampAsync(user);
        await userManager.RemovePasswordAsync(user);
        await sessions.EndAllAsync(user.Id, "admin-reset", cancellationToken);
        await resetMail.SendAsync(user, cancellationToken);

        logger.LogInformation("Administrator {AdminId} reset the password of user {UserId}", currentUser.UserId, command.UserId);
    }
}
