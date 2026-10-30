using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Infrastructure.Extensions;

namespace SignFlow.Infrastructure.Services.Auth;

public class TokenService(IOptions<JwtSettings> jwtSettings) : ITokenService
{
    private readonly JwtSettings _settings = jwtSettings.Value;

    /// <summary>The organisation the user belongs to (R-M0-4); read by TenantResolutionMiddleware.</summary>
    public const string TenantClaimType = "org";

    /// <summary>Present (value "required") while an Administrator or Manager still has to set up two-factor
    /// sign-in; MfaSetupGateMiddleware then lets the token reach only the setup endpoints.</summary>
    public const string MfaSetupClaimType = "mfa_setup";

    private static readonly TimeSpan MfaChallengeLifetime = TimeSpan.FromMinutes(5);

    // A challenge is signed with the same key but for another audience, so it can never pass as an access token.
    private string MfaAudience => _settings.Audience + "/mfa-challenge";

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(_settings.ExpirationMinutes);

    public string GenerateAccessToken(ApplicationUser user, IList<string> roles, bool mfaSetupRequired = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email!),
            new(ClaimTypes.Name, user.FullName),
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(TenantClaimType, user.OrganisationId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        if (mfaSetupRequired)
            claims.Add(new Claim(MfaSetupClaimType, "required"));

        return Write(claims, _settings.Audience, AccessTokenLifetime);
    }

    public string GenerateMfaChallenge(ApplicationUser user) =>
        Write([new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString())], MfaAudience, MfaChallengeLifetime);

    public Guid? ValidateMfaChallenge(string challenge)
    {
        var principal = Validate(challenge, MfaAudience);
        var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(subject, out var id) ? id : null;
    }

    public bool ValidateToken(string token) => Validate(token, _settings.Audience) is not null;

    private string Write(IEnumerable<Claim> claims, string audience, TimeSpan lifetime)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private ClaimsPrincipal? Validate(string token, string audience)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret)),
                ValidateIssuer = true,
                ValidIssuer = _settings.Issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }
}
