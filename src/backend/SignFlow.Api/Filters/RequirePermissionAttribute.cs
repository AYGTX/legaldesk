using Microsoft.AspNetCore.Authorization;

namespace SignFlow.Api.Filters;

/// <summary>
/// Requires a permission from <see cref="SignFlow.Application.Common.Security.Permissions"/>. Each permission is
/// registered as a policy of the same name at start-up (Program.cs), so this is plain ASP.NET authorisation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permission) : AuthorizeAttribute(permission)
{
    public string Permission { get; } = permission;
}
