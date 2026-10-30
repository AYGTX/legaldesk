using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace SignFlow.Api;

/// <summary>
/// Request limits, partitioned per caller (security baseline): sign-in per client IP, signing links per token,
/// everything else per signed-in user (or IP when anonymous). A refused request gets 429 with Retry-After.
/// </summary>
public static class RateLimits
{
    public const string Auth = "auth";
    public const string Signing = "signing";

    public static IServiceCollection AddLegalDeskRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // 10 attempts per 15 minutes per IP on sign-in, MFA, invitation and reset endpoints.
            options.AddPolicy(Auth, http => RateLimitPartition.GetFixedWindowLimiter(
                Ip(http), _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(15) }));

            // 60 calls per minute per signing link (the token is the third path segment after /api/v1/sign/).
            options.AddPolicy(Signing, http => RateLimitPartition.GetFixedWindowLimiter(
                SigningToken(http), _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));

            // Everything: 300 per minute per user, or per IP when anonymous.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Ip(http),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));

            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                return ValueTask.CompletedTask;
            };
        });

    private static string Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string SigningToken(HttpContext http)
    {
        var segments = http.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        return segments.Length >= 4 ? segments[3] : Ip(http);
    }
}
