using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// M0 acceptance: a user of organisation A never reads or writes organisation B's rows. Two contexts share one
/// database, each acting for its own organisation, the way two requests from two clients would.
/// </summary>
public class TenantIsolationTests : IDisposable
{
    private static readonly Guid OrgA = Guid.NewGuid();
    private static readonly Guid OrgB = Guid.NewGuid();

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly List<ApplicationDbContext> _contexts = [];

    private ApplicationDbContext ContextFor(Guid? organisationId)
    {
        var tenant = new TenantContext();
        if (organisationId is { } id) tenant.BeginScope(id);
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options, tenant);
        _contexts.Add(db);
        return db;
    }

    public void Dispose() => _contexts.ForEach(c => c.Dispose());

    [Fact]
    public void Every_organisation_owned_table_has_the_tenant_filter_and_a_foreign_key()
    {
        using var db = ContextFor(null);
        var owned = db.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)).ToList();

        Assert.Equal(28, owned.Count);
        Assert.All(owned, t =>
        {
            Assert.Contains(t.GetDeclaredQueryFilters(), f => f.Key == QueryFilters.Tenant);
            Assert.Contains(t.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Organisation));
        });
    }

    [Fact]
    public async Task A_contract_of_another_organisation_is_invisible_by_id_list_and_child_rows()
    {
        var (documentId, recipientId) = await SeedContractIn(OrgB);

        var a = ContextFor(OrgA);
        Assert.Null(await a.Documents.FindAsync(documentId));
        Assert.Empty(await a.Documents.ToListAsync());
        Assert.Empty(await a.Recipients.ToListAsync());
        Assert.False(await a.Recipients.AnyAsync(r => r.Id == recipientId));

        var b = ContextFor(OrgB);
        Assert.NotNull(await b.Documents.FindAsync(documentId));
        Assert.Single(await b.Recipients.ToListAsync());
    }

    [Fact]
    public async Task New_rows_take_the_current_organisation()
    {
        var (documentId, recipientId) = await SeedContractIn(OrgA);

        var unscoped = ContextFor(null);
        Assert.Equal(OrgA, (await unscoped.Documents.SingleAsync(d => d.Id == documentId)).OrganisationId);
        Assert.Equal(OrgA, (await unscoped.Recipients.SingleAsync(r => r.Id == recipientId)).OrganisationId);
    }

    [Fact]
    public async Task A_row_of_another_organisation_cannot_be_written()
    {
        var (documentId, _) = await SeedContractIn(OrgB);

        // Even a row obtained around the filter (here: an unscoped read) cannot be saved while acting for A.
        var document = await ContextFor(null).Documents.AsNoTracking().SingleAsync(d => d.Id == documentId);
        var a = ContextFor(OrgA);
        a.Documents.Attach(document);
        document.UpdateDetails("Taken over", null, null, null, null);
        a.Entry(document).State = EntityState.Modified;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => a.SaveChangesAsync());
        Assert.Contains("another organisation", error.Message);
    }

    [Fact]
    public async Task A_business_row_cannot_be_created_without_an_organisation()
    {
        var unscoped = ContextFor(null);
        unscoped.RequestTypes.Add(RequestType.Create("Loose", null, 24, null, null));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => unscoped.SaveChangesAsync());
        Assert.Contains("no organisation", error.Message);
    }

    [Fact]
    public async Task Users_of_another_organisation_are_invisible()
    {
        var seeding = ContextFor(OrgB);
        seeding.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), UserName = "b@b.tn", Email = "b@b.tn" });
        await seeding.SaveChangesAsync();

        Assert.Empty(await ContextFor(OrgA).Users.ToListAsync());
        Assert.Single(await ContextFor(OrgB).Users.ToListAsync());
    }

    [Fact]
    public async Task Platform_queries_resolve_a_signing_link_to_its_organisation_and_nothing_more()
    {
        var (_, recipientId) = await SeedContractIn(OrgB);
        var token = (await ContextFor(OrgB).Recipients.SingleAsync(r => r.Id == recipientId)).SecureToken;

        var platform = new PlatformQueries(ContextFor(OrgA));
        Assert.Equal(OrgB, await platform.OrganisationOfSigningTokenAsync(token));
        Assert.Null(await platform.OrganisationOfSigningTokenAsync("not-a-token"));
    }

    [Fact]
    public void Only_PlatformQueries_lifts_the_tenant_filter()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var backend = Path.Combine(root, "src", "backend");
        // A bare IgnoreQueryFilters() lifts every filter, the tenant one included.
        var liftsTenant = new Regex(@"IgnoreQueryFilters\(\s*\)|IgnoreQueryFilters\([^)]*Tenant");

        var offenders = Directory.EnumerateFiles(backend, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && Path.GetFileName(f) != "PlatformQueries.cs")
            .Where(f => liftsTenant.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    private async Task<(Guid DocumentId, Guid RecipientId)> SeedContractIn(Guid organisationId)
    {
        var db = ContextFor(organisationId);
        db.Organisations.Add(Organisation.Create($"Org {organisationId:N}", $"org-{organisationId:N}", organisationId));
        var document = Document.Create("Lease", "lease.pdf", "documents/lease.pdf", 10, 1, Guid.NewGuid());
        db.Documents.Add(document);
        var recipient = Recipient.Create(document.Id, "Signer", "signer@test.tn", 1, ApprovalType.Signature);
        db.Recipients.Add(recipient);
        await db.SaveChangesAsync();
        return (document.Id, recipient.Id);
    }
}
