using System.Threading.RateLimiting;
using Asp.Versioning;
using Hangfire;
using Hangfire.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using SignFlow.Application.Extensions;
using SignFlow.Infrastructure.Extensions;
using SignFlow.Infrastructure.Services.SignalR;
using SignFlow.Infrastructure.Services.Background;
using SignFlow.Api.Middleware;
using SignFlow.Infrastructure.Services.Pdf;

// PDFsharp's cross-platform core build has no built-in fonts/OS lookup and throws on first font use
// unless this is assigned — see https://docs.pdfsharp.net/link/font-resolving.html
PdfSharp.Fonts.GlobalFontSettings.FontResolver = new AppFontResolver();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} ({SourceContext}){NewLine}{Exception}")
    .WriteTo.File("logs/legaldesk-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting LegalDesk API...");

    var builder = WebApplication.CreateBuilder(args);
    // Machine-local settings that must never be committed (Seed:Users with passwords, keys). Optional.
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

    builder.Host.UseSerilog((context, services, config) =>
        config.ReadFrom.Configuration(context.Configuration)
              .ReadFrom.Services(services)
              .Enrich.FromLogContext()
              .WriteTo.Console()
              .WriteTo.File("logs/signflow-.log", rollingInterval: RollingInterval.Day));

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    }).AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

    builder.Services.AddControllers().AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

    builder.Services.AddOpenApi();

    builder.Services.AddRateLimiter(options =>
    {
        options.AddFixedWindowLimiter("api", config =>
        {
            config.Window = TimeSpan.FromMinutes(1);
            config.PermitLimit = 120;
            config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            config.QueueLimit = 10;
        });
        options.AddFixedWindowLimiter("auth", config =>
        {
            config.Window = TimeSpan.FromMinutes(15);
            config.PermitLimit = 10;
        });
        options.OnRejected = async (context, ct) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.HttpContext.Response.WriteAsync("Too many requests. Please try again later.", ct);
        };
    });

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
            policy.WithOrigins(
                    builder.Configuration.GetValue<string>("AllowedOrigins")?.Split(',') ?? ["http://localhost:4200"])
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
    });

    builder.Services.AddHealthChecks()
        .AddSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection")!,
            name: "database",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["db", "sql"])
        .AddHangfire(options => options.MinimumAvailableServers = 1, name: "hangfire", tags: ["background"]);

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("LegalDesk.API"))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation())
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation());

    var app = builder.Build();

    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseMiddleware<RequestContextMiddleware>();

    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
        options.GetLevel = (httpContext, elapsed, ex) => ex != null
            ? LogEventLevel.Error
            : httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error : LogEventLevel.Information;
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.Title = "LegalDesk API";
            options.Theme = ScalarTheme.DeepSpace;
        });
    }

    app.UseRateLimiter();
    app.UseCors("AllowFrontend");

    // Serve uploaded avatars publicly (only the avatars subfolder — PDFs/signatures stay private)
    var storageRoot = app.Configuration["Storage:LocalPath"] ?? "uploads";
    var avatarsPath = Path.Combine(app.Environment.ContentRootPath, storageRoot, "avatars");
    Directory.CreateDirectory(avatarsPath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(avatarsPath),
        RequestPath = "/avatars"
    });

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new HangfireAuthFilter(app.Environment.IsDevelopment())]
    });

    // Recurring SLA sweep: mark overdue in-progress requests as Expired every 15 minutes.
    RecurringJob.AddOrUpdate<SlaExpirationJob>(
        "sla-expiration-sweep",
        job => job.SweepAsync(CancellationToken.None),
        "*/15 * * * *");

    // Recurring obligation sweep: remind owners of upcoming contract renewal/expiry dates daily.
    RecurringJob.AddOrUpdate<ObligationReminderJob>(
        "obligation-reminder-sweep",
        job => job.SweepAsync(CancellationToken.None),
        "0 8 * * *");

    // Recurring contract term sweep: walk executed contracts through their expiry milestones and
    // escalate to the legal team once the notice window has closed. Runs an hour before the
    // obligation sweep so the two don't compete for the same connections.
    RecurringJob.AddOrUpdate<ContractTermAlertJob>(
        "contract-term-alert-sweep",
        job => job.SweepAsync(CancellationToken.None),
        "0 7 * * *");

    app.MapControllers();
    app.MapHub<SignFlowHub>("/hubs/signflow");

    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds
                })
            });
            await context.Response.WriteAsync(result);
        }
    });

    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("db") });
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

    await app.Services.SeedDatabaseAsync();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "LegalDesk API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// The dashboard can trigger and delete background jobs, so it is closed by default: in development only requests
/// from this machine get in; anywhere else only an authenticated Administrator does.
/// </summary>
public class HangfireAuthFilter(bool isDevelopment) : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        var http = Hangfire.Dashboard.AspNetCoreDashboardContextExtensions.GetHttpContext(context);

        if (isDevelopment)
        {
            var remote = http.Connection.RemoteIpAddress;
            return remote is not null && System.Net.IPAddress.IsLoopback(remote);
        }

        return http.User.Identity?.IsAuthenticated == true && http.User.IsInRole("Administrator");
    }
}
