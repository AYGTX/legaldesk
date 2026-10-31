using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Common.Security;

/// <summary>
/// Who may read a contract: an administrator of contracts, its owner, or one of its participants. One rule for the detail
/// page (GET documents/{id}) and for joining its live group (R-M7-7), so presence never shows a contract to someone the
/// page would refuse.
/// </summary>
public static class DocumentReadAccess
{
    public static bool Allows(ICurrentUserService user, Guid ownerId, IEnumerable<string> participantEmails) =>
        user.Can(Permissions.ManageAllContracts)
        || ownerId == user.UserId
        || participantEmails.Any(e => e == user.UserEmail);

    public static async Task<bool> CanReadAsync(IApplicationDbContext db, ICurrentUserService user, Guid documentId, CancellationToken ct = default)
    {
        var document = await db.Documents.AsNoTracking()
            .Where(d => d.Id == documentId && !d.IsDeleted)
            .Select(d => new { d.OwnerId, Emails = d.Recipients.Select(r => r.Email).ToList() })
            .FirstOrDefaultAsync(ct);
        return document is not null && Allows(user, document.OwnerId, document.Emails);
    }
}

/// <summary>The signed-in user of a SignalR connection, read from its claims: a hub call has no HTTP request of its own.</summary>
public sealed class ClaimsCurrentUser(ClaimsPrincipal? principal) : ICurrentUserService
{
    public Guid? UserId =>
        Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal?.FindFirstValue("sub"), out var id) ? id : null;
    public string? UserEmail => principal?.FindFirstValue(ClaimTypes.Email) ?? principal?.FindFirstValue("email");
    public string? UserName => principal?.FindFirstValue(ClaimTypes.Name);
    public bool IsAuthenticated => principal?.Identity?.IsAuthenticated ?? false;
    public bool IsInRole(string role) => principal?.IsInRole(role) ?? false;
    public IEnumerable<string> Roles => principal?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? [];
}
