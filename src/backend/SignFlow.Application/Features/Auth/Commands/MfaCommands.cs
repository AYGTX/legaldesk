using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Common.Security;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Auth.Commands;

/// <summary>
/// Two-factor sign-in with an authenticator app (TOTP, R-M1-3). The authenticator key is Identity's; recovery codes
/// are ours, 10 single-use codes stored only as SHA-256.
/// </summary>
public static partial class Mfa
{
    public const string TokenStore = "LegalDesk";
    public const string RecoveryCodesName = "RecoveryCodeHashes";
    public const string Issuer = "LegalDesk";
    public const int RecoveryCodeCount = 10;

    [GeneratedRegex(@"^\d{6}$")]
    public static partial Regex TotpCode();

    public static string Normalise(string code) => code.Replace(" ", "").Replace("-", "").Trim();

    public static async Task<IReadOnlyList<string>> IssueRecoveryCodesAsync(UserManager<ApplicationUser> users, ApplicationUser user)
    {
        // 10 characters from an alphabet without look-alikes (no 0/O, 1/l/I), shown as XXXXX-XXXXX.
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        var codes = Enumerable.Range(0, RecoveryCodeCount)
            .Select(_ => new string(RandomNumberGenerator.GetItems<char>(alphabet, 10)))
            .Select(c => $"{c[..5]}-{c[5..]}")
            .ToList();
        await users.SetAuthenticationTokenAsync(user, TokenStore, RecoveryCodesName,
            string.Join(';', codes.Select(c => SecureTokens.Hash(Normalise(c)))));
        return codes;
    }

    /// <summary>True and the code is spent when it is one of the user's unused recovery codes.</summary>
    public static async Task<bool> RedeemRecoveryCodeAsync(UserManager<ApplicationUser> users, ApplicationUser user, string code)
    {
        var stored = await users.GetAuthenticationTokenAsync(user, TokenStore, RecoveryCodesName);
        if (string.IsNullOrEmpty(stored)) return false;
        var hashes = stored.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!hashes.Remove(SecureTokens.Hash(Normalise(code)))) return false;
        await users.SetAuthenticationTokenAsync(user, TokenStore, RecoveryCodesName, string.Join(';', hashes));
        return true;
    }

    public static Task<bool> VerifyTotpAsync(UserManager<ApplicationUser> users, ApplicationUser user, string code) =>
        users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Normalise(code));
}

/// <summary>Second sign-in step: the challenge from the password step plus a TOTP or recovery code.</summary>
public record MfaLoginCommand(string Challenge, string Code, ClientInfo Client) : ICommand<AuthSession>;

public class MfaLoginCommandValidator : AbstractValidator<MfaLoginCommand>
{
    public MfaLoginCommandValidator()
    {
        RuleFor(x => x.Challenge).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
    }
}

public class MfaLoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    AuthSessions sessions,
    ITokenService tokens,
    ILogger<MfaLoginCommandHandler> logger) : ICommandHandler<MfaLoginCommand, AuthSession>
{
    public const string InvalidCode = "Invalid or expired code.";

    public async Task<AuthSession> HandleAsync(MfaLoginCommand command, CancellationToken cancellationToken = default)
    {
        var userId = tokens.ValidateMfaChallenge(command.Challenge)
                     ?? throw new UnauthorizedAccessException(InvalidCode);
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || !user.TwoFactorEnabled || await userManager.IsLockedOutAsync(user))
            throw new UnauthorizedAccessException(InvalidCode);

        var code = Mfa.Normalise(command.Code);
        var ok = Mfa.TotpCode().IsMatch(code)
            ? await Mfa.VerifyTotpAsync(userManager, user, code)
            : await Mfa.RedeemRecoveryCodeAsync(userManager, user, code);

        if (!ok)
        {
            // Wrong codes count towards the same lockout as wrong passwords.
            await userManager.AccessFailedAsync(user);
            throw new UnauthorizedAccessException(InvalidCode);
        }

        await userManager.ResetAccessFailedCountAsync(user);
        logger.LogInformation("User {UserId} signed in with a second factor", user.Id);
        return await sessions.StartAsync(user, command.Client, cancellationToken);
    }
}

/// <summary>Starts setup: a fresh authenticator key, returned with the otpauth:// address the QR code encodes.</summary>
public record BeginMfaSetupCommand : ICommand<MfaSetupDto>;

public record MfaSetupDto(string SharedKey, string AuthenticatorUri, string QrCodeDataUri);

public class BeginMfaSetupCommandHandler(UserManager<ApplicationUser> userManager, ICurrentUserService currentUser, IQrCodes qrCodes)
    : ICommandHandler<BeginMfaSetupCommand, MfaSetupDto>
{
    public async Task<MfaSetupDto> HandleAsync(BeginMfaSetupCommand command, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.LoadAsync(userManager, currentUser);
        if (user.TwoFactorEnabled)
            throw new ConflictException("Two-factor sign-in is already on. Turn it off first to change the app.");

        await userManager.ResetAuthenticatorKeyAsync(user);
        var key = await userManager.GetAuthenticatorKeyAsync(user)
                  ?? throw new InvalidOperationException("No authenticator key was generated.");
        var label = Uri.EscapeDataString($"{Mfa.Issuer}:{user.Email}");
        var uri = $"otpauth://totp/{label}?secret={key}&issuer={Uri.EscapeDataString(Mfa.Issuer)}&digits=6&period=30";
        return new MfaSetupDto(key, uri, qrCodes.PngDataUri(uri));
    }
}

/// <summary>Turns two-factor on once the user proves the app works; returns the recovery codes, shown only now.</summary>
public record ConfirmMfaSetupCommand(string Code) : ICommand<IReadOnlyList<string>>;

public class ConfirmMfaSetupCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUser,
    AuthSessions sessions) : ICommandHandler<ConfirmMfaSetupCommand, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> HandleAsync(ConfirmMfaSetupCommand command, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.LoadAsync(userManager, currentUser);
        if (user.TwoFactorEnabled)
            throw new ConflictException("Two-factor sign-in is already on.");
        if (!Mfa.TotpCode().IsMatch(Mfa.Normalise(command.Code)) || !await Mfa.VerifyTotpAsync(userManager, user, command.Code))
            throw new Common.Exceptions.ValidationException([new FluentValidation.Results.ValidationFailure("code", "The code does not match. Check the time on your phone and try again.")]);

        await userManager.SetTwoFactorEnabledAsync(user, true);
        var codes = await Mfa.IssueRecoveryCodesAsync(userManager, user);
        // Other devices signed in before two-factor must sign in again with it.
        await sessions.EndAllAsync(user.Id, "mfa-enabled", cancellationToken);
        return codes;
    }
}

/// <summary>Turns two-factor off with a valid code; refused where the organisation requires it for the user's role.</summary>
public record DisableMfaCommand(string Code) : ICommand;

public class DisableMfaCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUser,
    IOptions<SecurityOptions> security) : ICommandHandler<DisableMfaCommand>
{
    public async Task HandleAsync(DisableMfaCommand command, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUser.LoadAsync(userManager, currentUser);
        if (!user.TwoFactorEnabled) return;

        var roles = await userManager.GetRolesAsync(user);
        if (security.Value.RequireMfaForPrivilegedRoles && roles.Any(r => AuthSessions.PrivilegedRoles.Contains(r)))
            throw new ForbiddenAccessException("Your role requires two-factor sign-in.");

        if (!await Mfa.VerifyTotpAsync(userManager, user, command.Code))
            throw new Common.Exceptions.ValidationException([new FluentValidation.Results.ValidationFailure("code", "The code does not match.")]);

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await userManager.RemoveAuthenticationTokenAsync(user, Mfa.TokenStore, Mfa.RecoveryCodesName);
    }
}

internal static class CurrentUser
{
    public static async Task<ApplicationUser> LoadAsync(UserManager<ApplicationUser> users, ICurrentUserService current)
    {
        var id = current.UserId ?? throw new UnauthorizedAccessException("Sign in first.");
        return await users.FindByIdAsync(id.ToString()) ?? throw new UnauthorizedAccessException("Sign in first.");
    }
}
