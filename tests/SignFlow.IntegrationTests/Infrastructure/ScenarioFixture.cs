using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.Documents.Commands;
using SignFlow.Application.Features.Reviews;
using SignFlow.Application.Features.Recipients.Commands;
using SignFlow.Application.Features.SignatureFields.Commands;
using SignFlow.Application.Features.Signing.Commands;
using SignFlow.Domain.Entities;
using SignFlow.Infrastructure.Persistence;
using SignFlow.Infrastructure.Services.Background;
using SignFlow.Infrastructure.Services.Notifications;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Builds an in-memory service scope with all real command handlers and null stubs for
/// email / SignalR / storage / PDF / virus scan.  One fixture instance = one isolated DB.
/// </summary>
public sealed class ScenarioFixture : IDisposable
{
    private readonly ServiceProvider _provider;

    public IApplicationDbContext Db => _provider.GetRequiredService<IApplicationDbContext>();

    /// <summary>The organisation this fixture's data belongs to.</summary>
    public Guid OrganisationId { get; } = Guid.NewGuid();

    public ScenarioFixture(Guid ownerId, string ownerEmail = "owner@test.com")
    {
        var services = new ServiceCollection();

        // EF Core InMemory – each fixture gets its own DB so tests don't interfere
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // Every scenario acts for one organisation, as a signed-in request would (M0).
        services.AddScoped<ITenantContext>(_ =>
        {
            var tenant = new TenantContext();
            tenant.BeginScope(OrganisationId);
            return tenant;
        });
        services.AddScoped<IPlatformQueries, PlatformQueries>();

        // Identity, so a workflow step can be resolved from a role.
        services.AddLogging();
        services.AddIdentityCore<ApplicationUser>(o => o.Password.RequiredLength = 6)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<IWorkflowPresetResolver, WorkflowPresetResolver>();

        // Real infrastructure services
        services.AddScoped<IWorkflowEngine>(sp => new WorkflowEngine(
            sp.GetRequiredService<IApplicationDbContext>(),
            sp.GetRequiredService<IEmailService>(),
            sp.GetRequiredService<INotificationHub>(),
            sp.GetRequiredService<INotificationService>(),
            NullLogger<WorkflowEngine>.Instance));

        services.AddScoped<INotificationService>(sp => new NotificationService(
            sp.GetRequiredService<IApplicationDbContext>(),
            sp.GetRequiredService<INotificationHub>()));

        // Stubs
        services.AddSingleton<IEmailService, NullEmailService>();
        services.AddSingleton<INotificationHub, NullNotificationHub>();
        services.AddSingleton<IStorageService, NullStorageService>();
        services.AddSingleton<IPdfService, NullPdfService>();
        services.AddSingleton<IPdfStampService, NullPdfStampService>();
        services.AddSingleton<IHtmlTemplatePdfGenerator, NullHtmlTemplatePdfGenerator>();
        services.AddSingleton<IVirusScanService, NullVirusScanService>();
        services.AddSingleton<IPdfInspector, NullPdfInspector>();
        services.AddScoped(sp => new PdfUploadGuard(sp.GetRequiredService<IPdfInspector>(),
            sp.GetRequiredService<IVirusScanService>(), NullLogger<PdfUploadGuard>.Instance));
        services.AddSingleton<IAuditService, NullAuditService>();
        services.AddSingleton<IAiService, NullAiService>();
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(ownerId, ownerEmail));

        // Command handlers
        services.AddScoped(sp => new UploadDocumentCommandHandler(
            sp.GetRequiredService<IApplicationDbContext>(),
            sp.GetRequiredService<IStorageService>(),
            sp.GetRequiredService<IPdfService>(),
            sp.GetRequiredService<PdfUploadGuard>(),
            sp.GetRequiredService<IReviewQueue>(),
            sp.GetRequiredService<ICurrentUserService>(),
            sp.GetRequiredService<IAuditService>(),
            NullLogger<UploadDocumentCommandHandler>.Instance));

        services.AddScoped(sp => new ManageRecipientsCommandHandler(
            sp.GetRequiredService<IApplicationDbContext>(),
            sp.GetRequiredService<ICurrentUserService>()));

        services.AddScoped(sp => new SaveFieldsCommandHandler(
            sp.GetRequiredService<IApplicationDbContext>(),
            sp.GetRequiredService<IStorageService>(),
            sp.GetRequiredService<IPdfService>(),
            sp.GetRequiredService<ICurrentUserService>()));

        services.AddScoped(sp => new SendDocumentCommandHandler(
            sp.GetRequiredService<IApplicationDbContext>(),
            sp.GetRequiredService<IWorkflowEngine>(),
            sp.GetRequiredService<ICurrentUserService>(),
            sp.GetRequiredService<IAuditService>(),
            NullLogger<SendDocumentCommandHandler>.Instance));

        services.AddScoped(sp => new SignFieldCommandHandler(
            sp.GetRequiredService<IApplicationDbContext>(),
            sp.GetRequiredService<IStorageService>(),
            sp.GetRequiredService<IAuditService>(),
            NullLogger<SignFieldCommandHandler>.Instance));

        // Signing a field no longer advances the workflow — the recipient approves explicitly
        // once every required field is satisfied, and that step is what completes the document.
        services.AddScoped(sp => new ApproveSigningCommandHandler(
            sp.GetRequiredService<IApplicationDbContext>(),
            sp.GetRequiredService<IWorkflowEngine>(),
            sp.GetRequiredService<IStorageService>(),
            sp.GetRequiredService<IPdfStampService>(),
            sp.GetRequiredService<IHtmlTemplatePdfGenerator>(),
            sp.GetRequiredService<IAuditService>(),
            sp.GetRequiredService<INotificationHub>(),
            NullLogger<ApproveSigningCommandHandler>.Instance));

        // The pre-flight review (M8): Harvey faked, jobs recorded and run by the test, the real handlers in between.
        services.AddSingleton(Harvey);
        services.AddSingleton<ILegalCorpusClient>(Harvey);
        services.AddSingleton(ReviewQueue);
        services.AddSingleton<IReviewQueue>(ReviewQueue);
        services.AddSingleton(Text);
        services.AddSingleton<IContractTextProvider>(Text);
        services.AddScoped<IContractReviewService, ContractReviewService>();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new ReviewOptions()));
        services.AddScoped<StartReviewCommandHandler>();
        services.AddScoped<ReviewRunner>();
        services.AddScoped<DecideReviewItemCommandHandler>();
        services.AddScoped<GetLatestReviewQueryHandler>();
        services.AddScoped<ReviewFeedbackSender>();
        services.AddScoped<SetReviewSettingCommandHandler>();

        // Signing access (M5): the gate, and the decline handler the scenarios use.
        services.AddScoped<SignFlow.Application.Features.Signing.ISigningAccess, SignFlow.Application.Features.Signing.SigningAccess>();
        services.AddScoped<ICommandHandler<RejectDocumentCommand>, RejectDocumentCommandHandler>();
        services.AddScoped<SignFlow.IntegrationTests.Scenarios.RejectDocumentHandlerAccessor>();

        _provider = services.BuildServiceProvider();
    }

    public FakeHarvey Harvey { get; } = new();
    public RecordingReviewQueue ReviewQueue { get; } = new();
    public FixedTextProvider Text { get; } = new();

    public T GetHandler<T>() where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>Runs Harvey's review of the contract's current version to the end, as the API and its job would.</summary>
    public async Task<ReviewDto> ReviewAsync(Guid documentId)
    {
        // The intake job is not run here (ContractIntakeTests covers it): its reading is taken as done.
        var document = await Db.Documents.FirstAsync(d => d.Id == documentId);
        if (document.IntakePending)
        {
            document.CompleteIntake(Domain.Enums.ContractTextSource.Embedded, null);
            await Db.SaveChangesAsync();
        }

        var run = await GetHandler<StartReviewCommandHandler>().HandleAsync(new StartReviewCommand(documentId));
        await GetHandler<ReviewRunner>().RunAsync(run.Id);
        return await GetHandler<GetLatestReviewQueryHandler>().HandleAsync(new GetLatestReviewQuery(documentId));
    }

    public void Dispose() => _provider.Dispose();
}
