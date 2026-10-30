using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Common.Security;

/// <summary>
/// Who may do what, in one table (rework spec R-M1-7). Controllers use <c>[RequirePermission]</c>, handlers use
/// <see cref="PermissionExtensions.Can"/>; nothing outside this file names a role to decide a right.
/// </summary>
public static class Permissions
{
    public const string ManageUsers = "users.manage";
    /// <summary>Act on any contract of the organisation as if its owner (owner checks are skipped).</summary>
    public const string ManageAllContracts = "contracts.manageAll";
    /// <summary>Read any contract's pre-flight review (the legal team follows every contract's risks).</summary>
    public const string ReadAllReviews = "reviews.readAll";
    public const string ReopenContracts = "contracts.reopen";
    /// <summary>Accept a critical finding with a reason: a legal judgement, not a drafting fix.</summary>
    public const string AcceptCriticalRisks = "risks.acceptCritical";
    public const string EditClauses = "clauses.edit";
    public const string ApproveClauses = "clauses.approve";
    public const string ManageTemplates = "templates.manage";
    public const string EditRegulatoryTexts = "regulatory.edit";
    public const string PublishRegulatoryTexts = "regulatory.publish";
    public const string ManageRequestTypes = "requestTypes.manage";
    public const string ViewWorkflowPresets = "presets.view";
    public const string ManageWorkflowPresets = "presets.manage";
    public const string ViewReports = "reports.view";
    /// <summary>Switch rules of Harvey's grid off for the organisation, or change their severity (R-M0-7).</summary>
    public const string ManageReviewSettings = "reviewSettings.manage";

    public const string Administrator = "Administrator";
    public const string Manager = "Manager";
    public const string Employee = "Employee";

    private static readonly Dictionary<string, string[]> Grants = new()
    {
        [ManageUsers] = [Administrator],
        [ManageAllContracts] = [Administrator],
        [ReadAllReviews] = [Administrator, Manager],
        [ReopenContracts] = [Administrator],
        [AcceptCriticalRisks] = [Administrator, Manager],
        [EditClauses] = [Administrator, Manager],
        [ApproveClauses] = [Administrator],
        [ManageTemplates] = [Administrator, Manager],
        [EditRegulatoryTexts] = [Administrator, Manager],
        [PublishRegulatoryTexts] = [Administrator],
        [ManageRequestTypes] = [Administrator],
        [ViewWorkflowPresets] = [Administrator, Manager],
        [ManageWorkflowPresets] = [Administrator],
        [ViewReports] = [Administrator, Manager],
        [ManageReviewSettings] = [Administrator],
    };

    public static IReadOnlyCollection<string> All => Grants.Keys;

    public static IReadOnlyList<string> RolesFor(string permission) =>
        Grants.TryGetValue(permission, out var roles)
            ? roles
            : throw new ArgumentException($"Unknown permission '{permission}'.", nameof(permission));
}

public static class PermissionExtensions
{
    public static bool Can(this ICurrentUserService user, string permission) =>
        Permissions.RolesFor(permission).Any(user.IsInRole);
}
