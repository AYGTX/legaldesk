using SignFlow.Application.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Documents.Commands;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// Resolving the pre-flight review before a contract goes out: anyone who owns the draft can fix a finding or
/// set aside a minor one, but accepting a critical finding is a legal call — a manager, with a reason on record.
/// And fixing a finding means replacing the file, which has to work while the contract is still a draft.
/// </summary>
public class PreflightResolutionTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private sealed record Seed(ScenarioFixture Fixture, Document Document, DocumentReadinessItem Critical, DocumentReadinessItem Minor, FakeCurrentUserService User);

    private static async Task<Seed> SeedAsync(string[] roles)
    {
        var fixture = new ScenarioFixture(OwnerId);
        var db = (ApplicationDbContext)fixture.Db;

        var document = Document.Create("Supply agreement", "supply.pdf", "documents/supply.pdf", 10, 1, OwnerId);
        db.Documents.Add(document);

        var critical = DocumentReadinessItem.Create(document.Id, ReadinessItemCategory.Risk, "Critical",
            "risk:Liability", "Unlimited liability", "The provider accepts unlimited liability.");
        var minor = DocumentReadinessItem.Create(document.Id, ReadinessItemCategory.Structure, "Low",
            "risk:Notices", "No notices clause", "Add a notices clause.");
        db.DocumentReadinessItems.AddRange(critical, minor);
        await db.SaveChangesAsync();

        var user = (FakeCurrentUserService)fixture.GetHandler<ICurrentUserService>();
        user.RolesOverride = roles;
        return new Seed(fixture, document, critical, minor, user);
    }

    private static SetReadinessItemStatusCommandHandler Handler(ScenarioFixture f) =>
        new(f.Db, f.GetHandler<ICurrentUserService>(), f.GetHandler<IAuditService>());

    [Fact]
    public async Task An_employee_cannot_accept_a_critical_finding_but_can_fix_it_or_set_aside_a_minor_one()
    {
        var seed = await SeedAsync(["Employee"]);
        using var fixture = seed.Fixture;

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Handler(fixture).HandleAsync(
            new SetReadinessItemStatusCommand(seed.Document.Id, seed.Critical.Id, ReadinessItemStatus.Dismissed, "It is fine")));
        Assert.Equal(ReadinessItemStatus.Open, (await fixture.Db.DocumentReadinessItems.FirstAsync(i => i.Id == seed.Critical.Id)).Status);

        // Fixing the critical finding is anyone's job, and a minor one can be set aside without a manager.
        await Handler(fixture).HandleAsync(new SetReadinessItemStatusCommand(seed.Document.Id, seed.Critical.Id, ReadinessItemStatus.Resolved, null));
        await Handler(fixture).HandleAsync(new SetReadinessItemStatusCommand(seed.Document.Id, seed.Minor.Id, ReadinessItemStatus.Dismissed, null));

        var items = await fixture.Db.DocumentReadinessItems.ToListAsync();
        Assert.Equal(ReadinessItemStatus.Resolved, items.Single(i => i.Id == seed.Critical.Id).Status);
        Assert.Equal(ReadinessItemStatus.Dismissed, items.Single(i => i.Id == seed.Minor.Id).Status);
    }

    [Fact]
    public async Task A_manager_accepts_a_critical_finding_only_with_a_reason()
    {
        var seed = await SeedAsync(["Manager"]);
        using var fixture = seed.Fixture;

        await Assert.ThrowsAsync<ValidationException>(() => Handler(fixture).HandleAsync(
            new SetReadinessItemStatusCommand(seed.Document.Id, seed.Critical.Id, ReadinessItemStatus.Dismissed, "  ")));

        await Handler(fixture).HandleAsync(new SetReadinessItemStatusCommand(
            seed.Document.Id, seed.Critical.Id, ReadinessItemStatus.Dismissed, "Capped by the framework agreement."));

        var item = await fixture.Db.DocumentReadinessItems.FirstAsync(i => i.Id == seed.Critical.Id);
        Assert.Equal(ReadinessItemStatus.Dismissed, item.Status);
        Assert.Equal("Capped by the framework agreement.", item.ResolutionNote);
    }

    [Fact]
    public async Task The_file_of_a_draft_can_be_replaced_and_the_old_one_is_kept_as_version_1()
    {
        var seed = await SeedAsync(["Employee"]);
        using var fixture = seed.Fixture;

        var handler = new UploadRevisionCommandHandler(
            fixture.Db, fixture.GetHandler<IStorageService>(), fixture.GetHandler<IPdfService>(),
            fixture.GetHandler<PdfUploadGuard>(), fixture.GetHandler<ICurrentUserService>(),
            fixture.GetHandler<IAuditService>(), NullLogger<UploadRevisionCommandHandler>.Instance);

        var result = await handler.HandleAsync(new UploadRevisionCommand(
            seed.Document.Id, new MemoryStream(NullPdfInspector.FakePdf), "supply-v2.pdf", NullPdfInspector.FakePdf.Length, "application/pdf", "Liability capped"));

        Assert.Equal(2, result.Version);
        var document = await fixture.Db.Documents.Include(d => d.Versions).SingleAsync();
        Assert.Equal(DocumentStatus.Draft, document.Status);
        Assert.Equal("supply-v2.pdf", document.FileName);
        Assert.Equal(1, Assert.Single(document.Versions).VersionNumber);
    }
}
