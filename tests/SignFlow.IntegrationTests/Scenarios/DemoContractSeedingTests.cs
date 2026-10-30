using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.Reports.Queries;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;
using SignFlow.Infrastructure.Services.Pdf;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// Runs the real <see cref="DatabaseSeeder"/> with demo contracts switched on against an in-memory
/// database, then asks the same questions the demo will ask of it.
///
/// The point is not that the seeder compiles — it is that the portfolio it produces is coherent:
/// the PDFs it writes contain readable contract text, the executed contracts land on every rung of
/// the expiry ladder, and the readiness verdicts are the ones the production evaluator derives
/// from the stored findings rather than numbers a catalogue asserted.
/// </summary>
public class DemoContractSeedingTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ApplicationDbContext _db = null!;
    private InMemoryStorageService _storage = null!;

    public async Task InitializeAsync()
    {
        // PDFsharp has no font lookup of its own; Program.cs assigns this at startup.
        PdfSharp.Fonts.GlobalFontSettings.FontResolver ??= new AppFontResolver();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>(o => o.Password.RequiredLength = 6)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<ApplicationDbContext>();
        _storage = new InMemoryStorageService();

        await DatabaseSeeder.SeedAsync(
            _db,
            _provider.GetRequiredService<UserManager<ApplicationUser>>(),
            _provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
            new DatabaseSeeder.DemoContractSeedContext(_storage, new PdfPigService(NullLogger<PdfPigService>.Instance)),
            NullLogger.Instance);
    }

    public Task DisposeAsync()
    {
        _provider.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Seeding_produces_a_portfolio_across_every_contract_family_and_status()
    {
        var documents = await _db.Documents.Include(d => d.RequestType).ToListAsync();

        Assert.Equal(10, documents.Count);

        // All six contract families are represented, so the reporting breakdown is not one bar.
        Assert.Equal(6, documents.Select(d => d.RequestType!.Name).Distinct().Count());

        // Every outcome the reporting page can show has at least one contract behind it.
        foreach (var status in new[]
                 {
                     DocumentStatus.Draft, DocumentStatus.InProgress, DocumentStatus.Completed,
                     DocumentStatus.Rejected, DocumentStatus.ChangesRequested
                 })
        {
            Assert.Contains(documents, d => d.Status == status);
        }

        // Owned across two departments, which is what the by-department breakdown groups on.
        var departments = await _db.Documents.Select(d => d.Owner.Department).Distinct().ToListAsync();
        Assert.Equal(2, departments.Count);
    }

    [Fact]
    public async Task Every_seeded_contract_has_a_readable_pdf_behind_it()
    {
        var pdfService = new PdfPigService(NullLogger<PdfPigService>.Instance);
        var documents = await _db.Documents.ToListAsync();

        foreach (var document in documents)
        {
            Assert.True(await _storage.ExistsAsync(document.StoragePath),
                $"No file was written for '{document.Title}'");

            await using var stream = await _storage.DownloadAsync(document.StoragePath);
            var text = await pdfService.ExtractTextAsync(stream);

            // Text, not an image: this is what a live re-analysis reads.
            Assert.Contains("Article", text);
            Assert.True(text.Length > 500, $"'{document.Title}' extracted only {text.Length} characters");
            Assert.False(string.IsNullOrEmpty(document.Hash));
            Assert.True(document.PageCount >= 1);
        }
    }

    [Fact]
    public async Task Executed_contracts_cover_the_whole_expiry_alert_ladder()
    {
        var tracked = await _db.Documents
            .Where(d => d.Status == DocumentStatus.Completed && d.ContractEndDate != null)
            .ToListAsync();

        var stages = tracked
            .Select(d => ContractTermEvaluator.Evaluate(
                d.ContractStartDate, d.ContractEndDate, d.NoticePeriodDays, DateTime.UtcNow))
            .ToList();

        // The stage a legal team actually misses, and the two on either side of it.
        Assert.Contains(stages, s => s.Stage == LifecycleStage.NoticeDeadlinePassed);
        Assert.Contains(stages, s => s.Stage == LifecycleStage.ExpiringSoon);
        Assert.Contains(stages, s => s.Stage == LifecycleStage.Expired);
        Assert.Contains(stages, s => s.Stage == LifecycleStage.InForce);

        // Nothing has been alerted on yet, so each contract is armed at its current rung.
        var dueRungs = tracked
            .Zip(stages, (d, s) => ContractTermEvaluator.NextAlertStage(s, d.LastTermAlertStage))
            .Where(r => r is not null)
            .ToList();
        Assert.Equal(tracked.Count, dueRungs.Count);
        Assert.True(dueRungs.Distinct().Count() >= 4, "The ladder rungs should not all be the same one");
    }

    [Fact]
    public async Task The_lifecycle_report_has_something_to_show_and_no_review_is_seeded()
    {
        var lifecycle = await new GetContractLifecycleQueryHandler(_db).HandleAsync(new GetContractLifecycleQuery());
        Assert.True(lifecycle.TotalTracked >= 4);
        Assert.True(lifecycle.NoticeDeadlinePassed >= 1);
        Assert.NotEmpty(lifecycle.Upcoming);
        Assert.NotEmpty(lifecycle.ByRequestType);

        // Findings come only from Harvey's approved rules (D1): demo contracts are reviewed live, never seeded with one.
        var riskMap = await new GetRiskMapQueryHandler(_db).HandleAsync(new GetRiskMapQuery());
        Assert.Equal(0, riskMap.TotalAnalyzed);
        Assert.Empty(await _db.DocumentReadinessItems.ToListAsync());
        Assert.All(await _db.Documents.ToListAsync(), d => Assert.Equal(ReadinessVerdict.NotEvaluated, d.ReadinessVerdict));
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_the_portfolio()
    {
        await DatabaseSeeder.SeedAsync(
            _db,
            _provider.GetRequiredService<UserManager<ApplicationUser>>(),
            _provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
            new DatabaseSeeder.DemoContractSeedContext(_storage, new PdfPigService(NullLogger<PdfPigService>.Instance)),
            NullLogger.Instance);

        Assert.Equal(10, await _db.Documents.CountAsync());
        // 10 contract PDFs (the bank contract was added on 18 October) + the vacation request template's base PDF.
        Assert.Equal(11, _storage.Files.Count);
    }

    [Fact]
    public async Task Seeding_stays_off_unless_demo_contracts_are_asked_for()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>(o => o.Password.RequiredLength = 6)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();

        await DatabaseSeeder.SeedAsync(
            db,
            provider.GetRequiredService<UserManager<ApplicationUser>>(),
            provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>());

        Assert.Empty(await db.Documents.ToListAsync());
        // The reference data every install needs is still seeded.
        Assert.NotEmpty(await db.RequestTypes.ToListAsync());
        Assert.NotEmpty(await db.RegulatoryTexts.ToListAsync());
    }

    [Fact]
    public async Task Vacation_request_template_is_seeded_ready_to_send()
    {
        var template = await _db.PdfTemplates
            .Include(t => t.Fields)
            .Include(t => t.RequestType)
            .SingleAsync(t => t.Name == "Demande de congé");

        Assert.Equal("Demande de congé", template.RequestType!.Name);
        Assert.Equal(1, template.PageCount);
        Assert.Equal(16, template.Fields.Count);

        // Two pre-assigned approver signatures in order: line manager (stage 1), then HR (stage 2).
        var signatures = template.Fields
            .Where(f => f.Type == PdfFieldType.Signature)
            .OrderBy(f => f.Order)
            .ToList();
        Assert.Equal(["manager_signature", "hr_signature"], signatures.Select(f => f.FieldKey));
        Assert.All(signatures, f => Assert.Equal(PdfFieldFillRole.Approver, f.FillRole));
        Assert.All(signatures, f => Assert.NotNull(f.PreAssignedApproverId));
        Assert.Equal([1, 2], signatures.Select(f => f.WorkflowStage));

        // Every field fits on an A4 page rendered at PDF.js scale 1.5 (892.5 × 1263 canvas px).
        Assert.All(template.Fields, f =>
        {
            Assert.InRange(f.X + f.Width, 0, 892.5f);
            Assert.InRange(f.Y + f.Height, 0, 1263f);
        });

        // The stored base PDF is a real, readable form.
        var pdfService = new PdfPigService(NullLogger<PdfPigService>.Instance);
        var text = await pdfService.ExtractTextAsync(await _storage.DownloadAsync(template.StoragePath));
        Assert.Contains("DEMANDE DE CONGÉ", text);
        Assert.Contains("Signature du responsable", text);

        // A second seeding run does not duplicate it.
        await DatabaseSeeder.SeedAsync(
            _db,
            _provider.GetRequiredService<UserManager<ApplicationUser>>(),
            _provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
            new DatabaseSeeder.DemoContractSeedContext(_storage, pdfService),
            NullLogger.Instance);
        Assert.Equal(1, await _db.PdfTemplates.CountAsync(t => t.Name == "Demande de congé"));
    }

    [Fact]
    public async Task B2B_contract_template_switches_library_clauses_on_the_values_entered()
    {
        var template = await _db.HtmlTemplates
            .Include(t => t.Fields)
            .Include(t => t.RequestType)
            .SingleAsync(t => t.Name == "Contrat B2B — Prestation de services");

        Assert.Equal("Contrat B2B", template.RequestType!.Name);
        var signatures = template.Fields
            .Where(f => f.Type == HtmlTemplateFieldType.Signature)
            .OrderBy(f => f.Order)
            .ToList();
        Assert.Equal(["legal_signature", "client_signature"], signatures.Select(f => f.FieldKey));
        Assert.NotNull(signatures[0].PreAssignedApproverId);
        Assert.Null(signatures[1].PreAssignedApproverId); // the client is chosen per contract

        var types = template.Fields.ToDictionary(f => f.FieldKey, f => f.Type);
        var baseValues = new Dictionary<string, string>
        {
            ["client_company"] = "Société Test SARL",
            ["contract_value"] = "45000",
            ["start_date"] = "2026-11-01",
            ["duration_months"] = "12",
        };

        // Standard position: every switch off.
        var standard = HtmlContractRenderer.Render(template.HtmlContent, baseValues, types);
        Assert.Contains("ne saurait excéder", standard);                 // capped liability (library)
        Assert.Contains("durée ferme de 12 mois", standard);              // fixed term, no renewal
        Assert.Contains("aucun traitement de données", standard);
        Assert.DoesNotContain("tacite reconduction pour des périodes", standard);
        Assert.DoesNotContain("garantie bancaire", standard);
        Assert.DoesNotContain("sans limitation de montant", standard);
        Assert.DoesNotContain("{{", standard);

        // Every switch on: the corresponding library clauses (or the non-standard wording) appear.
        var switched = new Dictionary<string, string>(baseValues)
        {
            ["contract_value"] = "250000",
            ["auto_renewal"] = "true",
            ["personal_data"] = "true",
            ["ip_deliverables"] = "true",
            ["uncapped_liability"] = "true",
        };
        var risky = HtmlContractRenderer.Render(template.HtmlContent, switched, types);
        Assert.Contains("garantie bancaire", risky);
        Assert.Contains("tacite reconduction pour des périodes", risky);
        Assert.Contains("loi organique n° 2004-63", risky);
        Assert.Contains("éléments préexistants", risky);
        Assert.Contains("sans limitation de montant", risky);
        Assert.DoesNotContain("ne saurait excéder", risky);
        Assert.DoesNotContain("{{", risky);
    }
}
