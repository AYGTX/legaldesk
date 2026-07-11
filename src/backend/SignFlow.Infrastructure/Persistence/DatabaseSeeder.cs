using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SignFlow.Domain.Entities;

namespace SignFlow.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        // Roles
        string[] roles = ["Administrator", "Manager", "Employee", "ExternalSigner"];
        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
        }

        // Admin user
        if (await userManager.FindByEmailAsync("admin@signflow.app") is null)
        {
            var admin = new ApplicationUser
            {
                UserName = "admin@signflow.app",
                Email = "admin@signflow.app",
                FirstName = "System",
                LastName = "Administrator",
                Department = "IT",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(admin, "Admin@SignFlow1!");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(admin, "Administrator");
        }

        // Demo manager
        if (await userManager.FindByEmailAsync("manager@signflow.app") is null)
        {
            var manager = new ApplicationUser
            {
                UserName = "manager@signflow.app",
                Email = "manager@signflow.app",
                FirstName = "Demo",
                LastName = "Manager",
                Company = "Acme Corp",
                Department = "Sales",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(manager, "Manager@SignFlow1!");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(manager, "Manager");
        }

        // Demo employee
        if (await userManager.FindByEmailAsync("employee@signflow.app") is null)
        {
            var employee = new ApplicationUser
            {
                UserName = "employee@signflow.app",
                Email = "employee@signflow.app",
                FirstName = "Demo",
                LastName = "Employee",
                Company = "Acme Corp",
                Department = "Operations",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(employee, "Employee@SignFlow1!");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(employee, "Employee");
        }

        await context.SaveChangesAsync();

        await BackfillDepartmentsAsync(context);
        await SeedRequestTypesAsync(context);
    }

    private static async Task BackfillDepartmentsAsync(ApplicationDbContext context)
    {
        var departmentsByEmail = new Dictionary<string, string>
        {
            ["admin@signflow.app"] = "IT",
            ["manager@signflow.app"] = "Sales",
            ["employee@signflow.app"] = "Operations"
        };

        var usersMissingDepartment = await context.Users
            .Where(u => u.Department == null)
            .ToListAsync();

        foreach (var user in usersMissingDepartment)
        {
            user.Department = user.Email is not null && departmentsByEmail.TryGetValue(user.Email, out var dept)
                ? dept
                : "General";
        }

        if (usersMissingDepartment.Count > 0)
            await context.SaveChangesAsync();
    }

    private static async Task SeedRequestTypesAsync(ApplicationDbContext context)
    {
        // (Name, Description, SlaHours, Icon, Color)
        var defaults = new (string Name, string Description, int SlaHours, string Icon, string Color)[]
        {
            ("Vacation Request", "Request approval for planned time off. Submit at least one week in advance.", 72, "beach_access", "#22c55e"),
            ("Sick Leave", "Notify and obtain approval for medical or sick leave.", 48, "sick", "#ef4444"),
            ("Business Trip", "Authorization for work-related travel and associated bookings.", 96, "flight", "#3b82f6"),
            ("Purchase Approval", "Approval for purchasing goods or services. Time-sensitive.", 24, "shopping_cart", "#f59e0b"),
            ("Expense Reimbursement", "Reimbursement for business expenses with receipts attached.", 120, "receipt_long", "#8b5cf6"),
            ("Contract Signature", "Legal contract requiring signatures from all parties.", 168, "gavel", "#6366f1"),
        };

        var existing = await context.RequestTypes
            .Select(t => t.Name)
            .ToListAsync();

        var toAdd = defaults
            .Where(d => !existing.Contains(d.Name))
            .Select(d => RequestType.Create(d.Name, d.Description, d.SlaHours, d.Icon, d.Color))
            .ToList();

        if (toAdd.Count > 0)
        {
            context.RequestTypes.AddRange(toAdd);
            await context.SaveChangesAsync();
        }
    }
}
