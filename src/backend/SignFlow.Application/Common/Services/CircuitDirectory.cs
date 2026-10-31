using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Common.Services;

/// <summary>Who stands behind a circuit step: the members of a team or role step (R-M4-3), and the stand-in of
/// someone who is away (R-M1-8).</summary>
public interface ICircuitDirectory
{
    /// <summary>The active people who may act for a team or role step; empty for a named participant.</summary>
    Task<IReadOnlyList<ApplicationUser>> PoolMembersAsync(Guid? teamId, string? role, CancellationToken ct = default);

    /// <summary>Whether <paramref name="userId"/> may act for the team or role step.</summary>
    Task<bool> IsPoolMemberAsync(Recipient recipient, Guid userId, CancellationToken ct = default);

    /// <summary>The person acting for <paramref name="email"/>'s account while they are away on <paramref name="utcNow"/>,
    /// when they named one.</summary>
    Task<ApplicationUser?> AbsenceDelegateAsync(string email, DateTime utcNow, CancellationToken ct = default);
}

public class CircuitDirectory(IApplicationDbContext db, UserManager<ApplicationUser> userManager) : ICircuitDirectory
{
    public async Task<IReadOnlyList<ApplicationUser>> PoolMembersAsync(Guid? teamId, string? role, CancellationToken ct = default)
    {
        if (teamId is { } team)
        {
            var ids = db.TeamMembers.Where(m => m.TeamId == team).Select(m => m.UserId);
            return await db.Users.Where(u => u.IsActive && ids.Contains(u.Id)).OrderBy(u => u.LastName).ToListAsync(ct);
        }
        if (!string.IsNullOrWhiteSpace(role))
        {
            // Users are filtered by organisation; the role lookup is not, so keep only the ones this organisation sees.
            var holders = (await userManager.GetUsersInRoleAsync(role)).Where(u => u.IsActive).Select(u => u.Id).ToList();
            return await db.Users.Where(u => holders.Contains(u.Id)).OrderBy(u => u.LastName).ToListAsync(ct);
        }
        return [];
    }

    public async Task<bool> IsPoolMemberAsync(Recipient recipient, Guid userId, CancellationToken ct = default)
    {
        if (!recipient.IsPool) return false;
        var members = await PoolMembersAsync(recipient.PoolTeamId, recipient.PoolRole, ct);
        return members.Any(m => m.Id == userId);
    }

    public async Task<ApplicationUser?> AbsenceDelegateAsync(string email, DateTime utcNow, CancellationToken ct = default)
    {
        var day = utcNow.Date;
        var normalized = email.Trim().ToLower();
        var delegateId = await db.Vacations
            .Where(v => v.StartDate <= day && v.EndDate >= day && v.DelegateUserId != null && v.User.Email!.ToLower() == normalized)
            .Select(v => v.DelegateUserId)
            .FirstOrDefaultAsync(ct);
        if (delegateId is null) return null;
        return await db.Users.FirstOrDefaultAsync(u => u.Id == delegateId && u.IsActive, ct);
    }
}
