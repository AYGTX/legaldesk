using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Infrastructure.Persistence;

/// <summary>
/// Every read that crosses organisations (R-M0-5). Each one returns ids only, never business data, so lifting the
/// tenant filter here cannot leak a contract. Changes to this class need a second reviewer.
/// </summary>
public sealed class PlatformQueries(ApplicationDbContext db) : IPlatformQueries
{
    public Task<Guid?> OrganisationOfUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Users.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(u => u.Id == userId)
            .Select(u => (Guid?)u.OrganisationId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Guid?> OrganisationOfSigningTokenAsync(string secureToken, CancellationToken cancellationToken = default) =>
        db.Recipients.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(r => r.SecureToken == secureToken)
            .Select(r => (Guid?)r.OrganisationId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ActiveOrganisationIdsAsync(CancellationToken cancellationToken = default) =>
        await db.Organisations.Where(o => o.IsActive).Select(o => o.Id).ToListAsync(cancellationToken);

    public async Task<Guid?> SoleOrganisationIdAsync(CancellationToken cancellationToken = default)
    {
        var ids = await db.Organisations.Where(o => o.IsActive).Select(o => o.Id).Take(2).ToListAsync(cancellationToken);
        return ids.Count == 1 ? ids[0] : null;
    }

    public Task<bool> EmailInUseAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalised = email.Trim().ToUpperInvariant();
        return db.Users.IgnoreQueryFilters([QueryFilters.Tenant])
            .AnyAsync(u => u.NormalizedEmail == normalised, cancellationToken);
    }
}
