using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

internal sealed class FakeCurrentUserService(Guid userId, string email = "owner@test.com") : ICurrentUserService
{
    public Guid? UserId => userId;
    public string? UserEmail => email;
    public string? UserName => "Test Owner";
    public bool IsAuthenticated => true;
    public bool IsInRole(string role) => true;
    public IEnumerable<string> Roles => ["Administrator"];
}
