namespace SignFlow.Application.Common.Models;

/// <summary>Section "Security" of the configuration.</summary>
public class SecurityOptions
{
    /// <summary>
    /// Administrators and Managers must set up two-factor sign-in before using the app (R-M1-3). On by default;
    /// a developer machine may switch it off in appsettings.Development.json, never a shared deployment.
    /// </summary>
    public bool RequireMfaForPrivilegedRoles { get; set; } = true;

    /// <summary>Address of the web app, used to build the links sent by e-mail (invitations, reset, signing).</summary>
    public string PublicUrl { get; set; } = "http://localhost:4200";
}
