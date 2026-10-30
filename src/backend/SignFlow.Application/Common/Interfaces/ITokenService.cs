using SignFlow.Domain.Entities;

namespace SignFlow.Application.Common.Interfaces;

public interface ITokenService
{
    /// <summary>Lifetime of an access token; the session cookie renews it (R-M1-4).</summary>
    TimeSpan AccessTokenLifetime { get; }

    /// <param name="mfaSetupRequired">True for an Administrator or Manager who has not set up two-factor sign-in yet:
    /// the token then opens only the two-factor setup endpoints (R-M1-3).</param>
    string GenerateAccessToken(ApplicationUser user, IList<string> roles, bool mfaSetupRequired = false);

    /// <summary>A five-minute ticket proving the password was right, exchanged with a TOTP or recovery code.</summary>
    string GenerateMfaChallenge(ApplicationUser user);

    /// <summary>The user id the challenge was issued to, or null when it is invalid or expired.</summary>
    Guid? ValidateMfaChallenge(string challenge);

    bool ValidateToken(string token);
}
