namespace SignFlow.Application.Common.Interfaces;

public record ScanResult(bool IsSafe, string? ThreatName = null);

public interface IVirusScanService
{
    Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
}
