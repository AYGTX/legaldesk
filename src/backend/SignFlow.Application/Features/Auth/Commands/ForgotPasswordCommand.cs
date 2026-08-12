using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Auth.Commands;

public record ForgotPasswordCommand(string Email) : ICommand;

public class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public class ForgotPasswordCommandHandler(
    UserManager<ApplicationUser> userManager,
    IEmailService emailService,
    ILogger<ForgotPasswordCommandHandler> logger) : ICommandHandler<ForgotPasswordCommand>
{
    public async Task HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(command.Email);
        if (user is null) return; // Don't reveal that user doesn't exist

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        user.PasswordResetToken = token;
        user.PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(24);
        await userManager.UpdateAsync(user);

        await emailService.SendTemplateAsync(command.Email, "ForgotPassword", new { user.FullName, Token = Uri.EscapeDataString(token) }, "Reset your LegalDesk password", cancellationToken);

        logger.LogInformation("Password reset email sent to {Email}", command.Email);
    }
}
