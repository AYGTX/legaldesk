using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SignFlow.Api.Middleware;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Application.Features.Signing;
using SignFlow.Infrastructure.Persistence;
using SignFlow.Infrastructure.Services.Auth;

namespace SignFlow.IntegrationTests.Api;

/// <summary>R-M0-4: which organisation a request acts for, and that the scope ends with the request.</summary>
public class TenantResolutionMiddlewareTests
{
    private static readonly Guid Org = Guid.NewGuid();

    private sealed class FakePlatform(Guid? userOrg = null, Guid? tokenOrg = null, Guid? sessionOrg = null) : IPlatformQueries
    {
        public Task<Guid?> OrganisationOfUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(userOrg);
        // Links and sessions are looked up by the hash of what the request carries, never by the clear value.
        public Task<(Guid UserId, Guid OrganisationId)?> UserOfCalendarTokenAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult<(Guid UserId, Guid OrganisationId)?>(null);
        public Task<Guid?> OrganisationOfSigningTokenAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult(tokenHash == SecureTokens.Hash("tok") ? tokenOrg : null);
        public Task<Guid?> OrganisationOfSigningSessionAsync(string secretHash, CancellationToken ct = default) =>
            Task.FromResult(secretHash == SecureTokens.Hash("sess") ? sessionOrg : null);
        public Task<SealRecord?> FindSealAsync(string code, CancellationToken ct = default) => Task.FromResult<SealRecord?>(null);
        public Task<IReadOnlyList<Guid>> ActiveOrganisationIdsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<Guid?> SoleOrganisationIdAsync(CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task<bool> EmailInUseAsync(string email, CancellationToken ct = default) => Task.FromResult(false);
    }

    private static async Task<Guid?> SeenDuringRequest(HttpContext http, IPlatformQueries platform, TenantContext tenant)
    {
        Guid? seen = null;
        var middleware = new TenantResolutionMiddleware(_ => { seen = tenant.OrganisationId; return Task.CompletedTask; });
        await middleware.InvokeAsync(http, tenant, platform);
        return seen;
    }

    private static HttpContext SignedIn(params Claim[] claims) => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
    };

    [Fact]
    public async Task A_signed_in_user_acts_for_the_organisation_in_their_token_until_the_request_ends()
    {
        var tenant = new TenantContext();
        var http = SignedIn(new Claim(TokenService.TenantClaimType, Org.ToString()));

        Assert.Equal(Org, await SeenDuringRequest(http, new FakePlatform(), tenant));
        Assert.Null(tenant.OrganisationId);
    }

    [Fact]
    public async Task A_token_without_an_organisation_is_resolved_from_the_user()
    {
        var http = SignedIn(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        Assert.Equal(Org, await SeenDuringRequest(http, new FakePlatform(userOrg: Org), new TenantContext()));
    }

    [Fact]
    public async Task A_signing_link_acts_for_its_recipients_organisation()
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/v1/sign/tok/fields/123/sign";

        Assert.Equal(Org, await SeenDuringRequest(http, new FakePlatform(tokenOrg: Org), new TenantContext()));
    }

    [Fact]
    public async Task A_signing_session_acts_for_its_participants_organisation()
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/v1/sign/session/approve";
        http.Request.Headers[SigningAccess.SessionHeader] = "sess";

        Assert.Equal(Org, await SeenDuringRequest(http, new FakePlatform(sessionOrg: Org), new TenantContext()));
    }

    [Fact]
    public async Task An_unknown_signing_link_or_an_anonymous_request_acts_for_no_organisation()
    {
        var unknown = new DefaultHttpContext();
        unknown.Request.Path = "/api/v1/sign/nope";
        Assert.Null(await SeenDuringRequest(unknown, new FakePlatform(tokenOrg: Org), new TenantContext()));

        var login = new DefaultHttpContext();
        login.Request.Path = "/api/v1/auth/login";
        Assert.Null(await SeenDuringRequest(login, new FakePlatform(tokenOrg: Org), new TenantContext()));
    }
}
