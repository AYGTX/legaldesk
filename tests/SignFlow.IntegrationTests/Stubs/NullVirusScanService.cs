using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

internal sealed class NullVirusScanService : IVirusScanService
{
    public Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
        => Task.FromResult(new ScanResult(true));
}
