using System.Text;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Infrastructure.Persistence;
using SignFlow.Infrastructure.Services.Auth;
using SignFlow.Infrastructure.Services.Background;
using SignFlow.Infrastructure.Services.Cache;
using SignFlow.Infrastructure.Services.Email;
using SignFlow.Infrastructure.Services.Pdf;
using SignFlow.Infrastructure.Services.Security;
using SignFlow.Infrastructure.Services.Notifications;
using SignFlow.Infrastructure.Services.SignalR;
using SignFlow.Infrastructure.Services.Ai;
using SignFlow.Infrastructure.Services.Ai.Llm;
using SignFlow.Infrastructure.Services.Storage;

namespace SignFlow.Infrastructure.Extensions;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)
                    .EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // Tenancy (M0): one tenant context per request or job, read by the DbContext's filters.
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IPlatformQueries, PlatformQueries>();

        // Identity
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 10;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        // Password reset links (and other Identity data-protection tokens) work for 60 minutes (R-M1-6).
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromMinutes(60));
        services.Configure<SignFlow.Application.Common.Models.SecurityOptions>(configuration.GetSection("Security"));

        // Settings
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.Configure<StorageSettings>(configuration.GetSection("Storage"));
        services.Configure<EmailSettings>(configuration.GetSection("Email"));
        services.Configure<GroqSettings>(configuration.GetSection("Groq"));
        services.Configure<LocalLlmSettings>(configuration.GetSection("LocalLlm"));
        services.Configure<LegalCorpusSettings>(configuration.GetSection("LegalCorpus"));

        // JWT Authentication
        var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
        // The signing key is a secret: it lives in user secrets (development) or the Jwt__Secret environment
        // variable, never in appsettings. Refuse to start rather than sign tokens with an empty or short key.
        if (string.IsNullOrWhiteSpace(jwtSettings.Secret) || jwtSettings.Secret.Length < 32)
            throw new InvalidOperationException(
                "Jwt:Secret is missing or shorter than 32 characters. Set it with `dotnet user-secrets set Jwt:Secret <value>` " +
                "in development, or the Jwt__Secret environment variable elsewhere.");
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // Support SignalR token in query string
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        context.Token = accessToken;
                    return Task.CompletedTask;
                }
            };
        });

        // Redis
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConnection))
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // Hangfire
        services.AddHangfire(config =>
            config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                  .UseSimpleAssemblyNameTypeSerializer()
                  .UseRecommendedSerializerSettings()
                  .UseSqlServerStorage(configuration.GetConnectionString("DefaultConnection"),
                    new SqlServerStorageOptions
                    {
                        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                        QueuePollInterval = TimeSpan.Zero,
                        UseRecommendedIsolationLevel = true,
                        DisableGlobalLocks = true
                    }));
        services.AddHangfireServer();
        services.AddScoped<SlaExpirationJob>();
        services.AddScoped<ObligationReminderJob>();
        services.AddScoped<ContractTermAlertJob>();
        services.AddScoped<OutboxDispatcherJob>();

        // SignalR
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = 128 * 1024;
        });

        // Language model strategy: Ai:Provider picks Groq, a local OpenAI-compatible server (Ollama), or None.
        var aiProvider = configuration.GetSection("Ai").Get<AiSettings>()?.Provider ?? "Groq";
        var localLlm = configuration.GetSection("LocalLlm").Get<LocalLlmSettings>() ?? new LocalLlmSettings();
        var isLocal = aiProvider.Equals("Local", StringComparison.OrdinalIgnoreCase);

        services.AddHttpClient(OpenAiCompatibleProvider.QuickClient, client =>
            client.Timeout = TimeSpan.FromSeconds(isLocal ? 120 : 30));
        // Full contract review runs for tens of seconds (minutes on a local model) — it gets its own client so
        // that summaries and template generation keep failing fast on the one above.
        services.AddHttpClient(OpenAiCompatibleProvider.AnalysisClient, client =>
            client.Timeout = TimeSpan.FromSeconds(isLocal ? localLlm.AnalysisTimeoutSeconds : 120));

        switch (aiProvider.ToLowerInvariant())
        {
            case "local": services.AddScoped<ILlmProvider, LocalLlmProvider>(); break;
            case "none": services.AddScoped<ILlmProvider, NoLlmProvider>(); break;
            case "groq": services.AddScoped<ILlmProvider, GroqLlmProvider>(); break;
            default: throw new InvalidOperationException($"Unknown Ai:Provider '{aiProvider}'. Use Groq, Local or None.");
        }

        var corpusSettings = configuration.GetSection("LegalCorpus").Get<LegalCorpusSettings>() ?? new LegalCorpusSettings();
        services.AddHttpClient(LegalCorpusClient.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(corpusSettings.TimeoutSeconds));
        services.AddScoped<ILegalCorpusClient, LegalCorpusClient>();
        services.Configure<SignFlow.Application.Features.Reviews.ReviewOptions>(configuration.GetSection("Review"));
        services.AddScoped<IReviewQueue, SignFlow.Infrastructure.Services.Background.HangfireReviewQueue>();

        // Text recognition for scanned contracts (R-M2-4): Tesseract and pdftoppm when installed, else none.
        services.Configure<SignFlow.Infrastructure.Services.Pdf.OcrSettings>(configuration.GetSection("Ocr"));
        services.AddSingleton<IOcrService, SignFlow.Infrastructure.Services.Pdf.TesseractOcrService>();

        // Application services
        services.AddScoped<IAiService, LlmAiService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IStorageService, LocalStorageService>();
        // Features queue mail in the outbox; only the dispatcher job talks to SMTP (R-M4-9).
        services.AddScoped<IEmailService, OutboxEmailService>();
        services.AddScoped<ISmtpSender, MailKitEmailService>();
        services.AddSingleton<IQrCodes, SignFlow.Infrastructure.Services.Pdf.QrCodes>();
        services.AddScoped<IPdfService, PdfPigService>();
        services.AddScoped<ICacheService, RedisCacheService>();
        services.AddScoped<IAuditService, AuditService>();
        // Uploads (R-M2-1, R-M2-2): ClamAV when Security:ClamAv:Host is set; without it, uploads are refused unless
        // Security:ClamAv:Required is false (developer machines), where only the file-type check runs.
        services.Configure<ClamAvOptions>(configuration.GetSection("Security:ClamAv"));
        var clamAv = configuration.GetSection("Security:ClamAv").Get<ClamAvOptions>() ?? new ClamAvOptions();
        if (!string.IsNullOrWhiteSpace(clamAv.Host))
            services.AddScoped<IVirusScanService, ClamAvScanService>();
        else if (clamAv.Required)
            services.AddScoped<IVirusScanService, MissingScannerService>();
        else
            services.AddScoped<IVirusScanService, VirusScanService>();
        services.AddSingleton<SignFlow.Application.Common.Services.IPdfInspector, PdfInspector>();
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<IPdfStampService, PdfStampService>();
        services.AddScoped<IPdfTextEditService, PdfTextEditService>();
        services.AddScoped<IHtmlTemplatePdfGenerator, HtmlTemplatePdfGenerator>();
        services.AddScoped<INotificationHub, NotificationHubService>();
        services.AddScoped<INotificationService, NotificationService>();

        return services;
    }

    public static async Task SeedDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseSeeder");

        // Off unless asked for: the demo portfolio is business data, and a production database
        // must never acquire nine contracts and a signed audit trail by starting the API.
        var demoContracts = configuration.GetValue<bool>("Demo:SeedContracts")
            ? new DatabaseSeeder.DemoContractSeedContext(
                scope.ServiceProvider.GetRequiredService<IStorageService>(),
                scope.ServiceProvider.GetRequiredService<IPdfService>())
            : null;

        await context.Database.MigrateAsync();
        var seedUsers = new SeedUsersOptions(
            configuration.GetValue("Seed:DefaultUsers", true),
            configuration.GetSection("Seed:Users").Get<List<SeedUser>>() ?? []);
        await DatabaseSeeder.SeedAsync(context, userManager, roleManager, demoContracts, logger, seedUsers);
    }
}
