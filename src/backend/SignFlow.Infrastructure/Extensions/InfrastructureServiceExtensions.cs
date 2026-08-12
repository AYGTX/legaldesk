using System.Text;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        // Identity
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 8;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        // Settings
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.Configure<StorageSettings>(configuration.GetSection("Storage"));
        services.Configure<EmailSettings>(configuration.GetSection("Email"));
        services.Configure<GroqSettings>(configuration.GetSection("Groq"));

        // JWT Authentication
        var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
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

        // SignalR
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = 128 * 1024;
        });

        // Named HttpClient for Groq (sets a sensible timeout)
        services.AddHttpClient("groq", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Full contract review runs for tens of seconds — it gets its own client so that
        // summaries and template generation keep failing fast on the 30s one above.
        services.AddHttpClient("groq-analysis", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        // Application services
        services.AddScoped<IAiService, GroqAiService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IStorageService, LocalStorageService>();
        services.AddScoped<IEmailService, MailKitEmailService>();
        services.AddScoped<IPdfService, PdfPigService>();
        services.AddScoped<ICacheService, RedisCacheService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IVirusScanService, VirusScanService>();
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<IPdfStampService, PdfStampService>();
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

        await context.Database.MigrateAsync();
        await DatabaseSeeder.SeedAsync(context, userManager, roleManager);
    }
}
