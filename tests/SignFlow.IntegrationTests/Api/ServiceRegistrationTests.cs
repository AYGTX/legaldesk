using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SignFlow.Application.Extensions;
using SignFlow.Infrastructure.Extensions;
using SignFlow.Infrastructure.Services.Background;
using SignFlow.Infrastructure.Services.Email;

namespace SignFlow.IntegrationTests.Api;

/// <summary>
/// The API's own registrations, with the API's own settings: every service can be built, and every background job can
/// be activated. A handler or job with a missing dependency otherwise fails only at run time, on the request or job
/// that first needs it. Nothing is resolved, so no database or Hangfire storage is touched.
/// </summary>
public class ServiceRegistrationTests
{
    private static IServiceCollection ApiServices()
    {
        var settings = FindApiSettings();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settings, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\none;Database=none;Trusted_Connection=True",
                ["ConnectionStrings:Redis"] = "localhost:1,abortConnect=false",
                ["Jwt:Secret"] = new string('k', 48)
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IWebHostEnvironment>(new FakeEnvironment());
        services.AddSingleton<IHostEnvironment>(sp => sp.GetRequiredService<IWebHostEnvironment>());
        services.AddSingleton<IHostApplicationLifetime, FakeLifetime>();
        services.AddHttpContextAccessor();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        return services;
    }

    [Fact]
    public void Every_registered_service_can_be_built()
    {
        using var provider = ApiServices().BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.NotNull(provider);
    }

    [Theory]
    [InlineData(typeof(ReviewRunJob))]
    [InlineData(typeof(ReviewFeedbackJob))]
    [InlineData(typeof(ContractIntakeJob))]
    [InlineData(typeof(SealDocumentJob))]
    [InlineData(typeof(OutboxDispatcherJob))]
    public void Every_dependency_of_a_background_job_is_registered(Type job)
    {
        using var provider = ApiServices().BuildServiceProvider();
        var isService = provider.GetRequiredService<IServiceProviderIsService>();

        var missing = job.GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType)
            .Where(t => !isService.IsService(t))
            .ToList();

        Assert.Empty(missing);
    }

    private static string FindApiSettings()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "backend", "SignFlow.Api", "appsettings.json");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("SignFlow.Api/appsettings.json not found above the test output folder.");
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ApplicationName { get; set; } = "SignFlow.Api";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Development";
    }
}
