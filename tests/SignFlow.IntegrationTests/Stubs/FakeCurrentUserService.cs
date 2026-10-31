using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

internal sealed class FakeCurrentUserService(Guid userId, string email = "owner@test.com") : ICurrentUserService
{
    /// <summary>Lets a test act as someone else without rebuilding the fixture.</summary>
    public Guid? UserIdOverride { get; set; }
    public Guid? UserId => UserIdOverride ?? userId;
    public string? EmailOverride { get; set; }
    public string? UserEmail => EmailOverride ?? email;
    public string? UserName => "Test Owner";
    public bool IsAuthenticated => true;
    /// <summary>When set, the user holds exactly these roles; otherwise every role (the default for scenario tests).</summary>
    public string[]? RolesOverride { get; set; }
    public bool IsInRole(string role) => RolesOverride?.Contains(role) ?? true;
    public IEnumerable<string> Roles => ["Administrator"];
}
