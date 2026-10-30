using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Api.Controllers.V1;
using SignFlow.Api.Filters;
using SignFlow.Application.Common.Security;

namespace SignFlow.IntegrationTests.Api;

/// <summary>
/// R-M1-7: rights are declared in one table and every endpoint says who may call it. These tests read the code
/// itself, so a new endpoint or a new role check that skips the table fails the build.
/// </summary>
public class AccessRulesTests
{
    private static readonly Assembly Api = typeof(DocumentsController).Assembly;

    private static IEnumerable<MethodInfo> Actions() =>
        Api.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any());

    [Fact]
    public void Every_endpoint_declares_who_may_call_it()
    {
        var undeclared = Actions()
            .Where(m => !m.IsDefined(typeof(IAuthorizeData), true)
                        && !m.IsDefined(typeof(IAllowAnonymous), true)
                        && !m.DeclaringType!.IsDefined(typeof(IAuthorizeData), true)
                        && !m.DeclaringType!.IsDefined(typeof(IAllowAnonymous), true))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.Empty(undeclared);
    }

    [Fact]
    public void Public_endpoints_are_rate_limited()
    {
        // Anonymous endpoints are where guessing happens: sign-in, recovery, signing links (security baseline).
        var unlimited = Actions()
            .Where(m => m.IsDefined(typeof(IAllowAnonymous), true) || m.DeclaringType!.IsDefined(typeof(IAllowAnonymous), true))
            .Where(m => !m.DeclaringType!.IsDefined(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), true)
                        && !m.IsDefined(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), true))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.Empty(unlimited);
    }

    [Fact]
    public void Every_permission_on_an_endpoint_exists_in_the_table()
    {
        var used = Actions()
            .SelectMany(m => m.GetCustomAttributes<RequirePermissionAttribute>()
                .Concat(m.DeclaringType!.GetCustomAttributes<RequirePermissionAttribute>()))
            .Select(a => a.Permission)
            .Distinct();

        Assert.All(used, p => Assert.Contains(p, Permissions.All));
    }

    [Fact]
    public void No_handler_or_controller_names_a_role_to_decide_a_right()
    {
        var backend = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "backend"));
        var roleCheck = new Regex(@"IsInRole\(""|Roles\s*=\s*""");
        string[] allowed = ["Permissions.cs", "Program.cs", "DatabaseSeeder.cs"];

        var offenders = Directory.EnumerateFiles(backend, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !allowed.Contains(Path.GetFileName(f)))
            .Where(f => roleCheck.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }
}
