using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Auth.Commands;

public record ForgotPasswordCommand(string Email) : ICommand;

public class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}

/// <summary>
/// Sends a reset link (R-M1-6). The token is Identity's: valid 60 minutes (DataProtectionTokenProviderOptions) and
/// spent by the reset, which changes the security stamp. Nothing is stored in clear.
/// </summary>
public class ForgotPasswordCommandHandler(
    UserManager<ApplicationUser> userManager,
    PasswordResetMail resetMail,
    ILogger<ForgotPasswordCommandHandler> logger) : ICommandHandler<ForgotPasswordCommand>
{
    public async Task HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(command.Email);
        // Same answer whether or not the address has an account, and nothing sent to deactivated accounts.
        if (user is null || !user.IsActive) return;

        await resetMail.SendAsync(user, cancellationToken);
        logger.LogInformation("Password reset link sent to user {UserId}", user.Id);
    }
}

/// <summary>Builds and queues the reset e-mail; used by "forgot password" and by an Administrator's reset.</summary>
public class PasswordResetMail(
    UserManager<ApplicationUser> userManager,
    IEmailService email,
    ITenantContext tenant,
    IOptions<SecurityOptions> security)
{
    public async Task SendAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = $"{security.Value.PublicUrl.TrimEnd('/')}/auth/reset-password" +
                   $"?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";

        // The request may be anonymous; the queued e-mail belongs to the user's organisation.
        using var scope = tenant.BeginScope(user.OrganisationId);
        await email.SendTemplateAsync(user.Email!, "ForgotPassword", new { user.FullName, Link = link },
            "Reset your LegalDesk password", cancellationToken);
    }
}
