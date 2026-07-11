using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Infrastructure.Services.Security;

/// <summary>
/// Stub virus scanner — replace with ClamAV, Windows Defender, or a cloud-based scanner in production.
/// </summary>
public class VirusScanService(ILogger<VirusScanService> logger) : IVirusScanService
{
    private static readonly string[] DangerousExtensions = [".exe", ".bat", ".cmd", ".sh", ".ps1", ".js", ".vbs", ".com", ".scr"];

    public Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (DangerousExtensions.Contains(ext))
        {
            logger.LogWarning("Rejected file with dangerous extension: {FileName}", fileName);
            return Task.FromResult(new ScanResult(false, "DangerousFileType"));
        }

        logger.LogDebug("File {FileName} passed virus scan", fileName);
        return Task.FromResult(new ScanResult(true));
    }
}
