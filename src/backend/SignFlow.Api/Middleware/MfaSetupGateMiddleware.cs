using System.Text.Json;
using SignFlow.Infrastructure.Services.Auth;

namespace SignFlow.Api.Middleware;

/// <summary>
/// R-M1-3: an Administrator or Manager without two-factor sign-in gets a token marked mfa_setup=required, and with it
/// reaches only their profile, the two-factor setup and sign-out. Everything else answers 403 code mfa.setupRequired,
/// which sends the web app to the setup screen.
/// </summary>
public class MfaSetupGateMiddleware(RequestDelegate next)
{
    private static readonly string[] Open =
    [
        "/api/v1/profile/mfa",
        "/api/v1/auth",
        "/health"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var mustSetUp = context.User.Identity?.IsAuthenticated == true
                        && context.User.HasClaim(TokenService.MfaSetupClaimType, "required");
        var path = context.Request.Path.Value ?? "";
        var allowed = Open.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                      || (HttpMethods.IsGet(context.Request.Method) && path.Equals("/api/v1/profile", StringComparison.OrdinalIgnoreCase));

        if (!mustSetUp || allowed)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            title = "Two-factor sign-in required",
            status = 403,
            detail = "Set up two-factor sign-in before using LegalDesk.",
            code = "mfa.setupRequired"
        }));
    }
}
