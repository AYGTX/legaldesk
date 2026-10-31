using SignFlow.Application.Common.Security;
using SignFlow.Application.Features.Signing;
using System.Security.Claims;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Infrastructure.Services.Auth;

namespace SignFlow.Api.Middleware;

/// <summary>
/// Sets the organisation a request acts for (R-M0-4), after authentication and before any endpoint runs:
/// a signed-in user acts for their own organisation; a signing link acts for its recipient's organisation.
/// Sign-in, recovery and health checks run without one.
/// </summary>
public class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenant, IPlatformQueries platform)
    {
        var organisationId = await ResolveAsync(context, platform);
        if (organisationId is null)
        {
            await next(context);
            return;
        }

        using (tenant.BeginScope(organisationId.Value))
            await next(context);
    }

    private static async Task<Guid?> ResolveAsync(HttpContext context, IPlatformQueries platform)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            if (Guid.TryParse(user.FindFirstValue(TokenService.TenantClaimType), out var fromClaim) && fromClaim != Guid.Empty)
                return fromClaim;

            // Tokens issued before multi-tenancy carry no organisation: look it up once per request.
            return Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                ? await platform.OrganisationOfUserAsync(userId, context.RequestAborted)
                : null;
        }

        // Public signing endpoints: /api/v{n}/sign/{token}/..., acting through a signing session when one is sent.
        var segments = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (segments.Length >= 4
            && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals("sign", StringComparison.OrdinalIgnoreCase))
        {
            var session = context.Request.Headers[SigningAccess.SessionHeader].ToString();
            if (!string.IsNullOrWhiteSpace(session))
                return await platform.OrganisationOfSigningSessionAsync(SecureTokens.Hash(session), context.RequestAborted);
            return await platform.OrganisationOfSigningTokenAsync(SecureTokens.Hash(segments[3]), context.RequestAborted);
        }

        return null;
    }
}
