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
        if (await userManager.FindByEmailAsync("admin@legaldesk.tn") is null)
        {
            var admin = new ApplicationUser
            {
                UserName = "admin@legaldesk.tn",
                Email = "admin@legaldesk.tn",
                FirstName = "System",
                LastName = "Administrator",
                Department = "Legal",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(admin, "Admin@LegalDesk1!");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(admin, "Administrator");
        }

        // Demo manager
        if (await userManager.FindByEmailAsync("manager@legaldesk.tn") is null)
        {
            var manager = new ApplicationUser
            {
                UserName = "manager@legaldesk.tn",
                Email = "manager@legaldesk.tn",
                FirstName = "Demo",
                LastName = "Manager",
                Company = "DCWI",
                Department = "Legal",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(manager, "Manager@LegalDesk1!");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(manager, "Manager");
        }

        // Demo employee
        if (await userManager.FindByEmailAsync("employee@legaldesk.tn") is null)
        {
            var employee = new ApplicationUser
            {
                UserName = "employee@legaldesk.tn",
                Email = "employee@legaldesk.tn",
                FirstName = "Demo",
                LastName = "Employee",
                Company = "DCWI",
                Department = "Procurement",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(employee, "Employee@LegalDesk1!");
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
            ["admin@legaldesk.tn"] = "Legal",
            ["manager@legaldesk.tn"] = "Legal",
            ["employee@legaldesk.tn"] = "Procurement"
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
        // Contract families, not HR requests: these drive the SLA clock and the
        // "by contract type" breakdown on the reports page.
        var defaults = new (string Name, string Description, int SlaHours, string Icon, string Color)[]
        {
            ("Contrat B2B", "Contrat commercial entre entreprises. Revue juridique obligatoire avant signature.", 168, "handshake", "#6366f1"),
            ("Contrat B2C", "Contrat de service destiné aux clients particuliers. Vérification de conformité consommateur.", 72, "person", "#22c55e"),
            ("Contrat DCWI", "Contrat lié à l'activité DCWI. Revue réglementaire télécoms requise.", 120, "cell_tower", "#0ea5e9"),
            ("NDA", "Accord de confidentialité. Circuit court, signature des deux parties.", 48, "lock", "#f59e0b"),
            ("Avenant", "Modification d'un contrat existant. Référence au contrat d'origine obligatoire.", 96, "edit_document", "#8b5cf6"),
            ("Contrat fournisseur", "Contrat d'achat ou de prestation. Validation achats puis juridique.", 168, "local_shipping", "#ef4444"),
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
