using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Services;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var value = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("sub");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? UserEmail => User?.FindFirstValue(ClaimTypes.Email) ?? User?.FindFirstValue("email");
    public string? UserName => User?.FindFirstValue(ClaimTypes.Name);
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) => User?.IsInRole(role) ?? false;

    public IEnumerable<string> Roles =>
        User?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? [];
}
