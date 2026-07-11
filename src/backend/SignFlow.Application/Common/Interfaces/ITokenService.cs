using System.Security.Claims;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Common.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(ApplicationUser user, IList<string> roles);
    string GenerateRefreshToken();
    ClaimsPrincipal GetPrincipalFromExpiredToken(string token);
    bool ValidateToken(string token);
}
