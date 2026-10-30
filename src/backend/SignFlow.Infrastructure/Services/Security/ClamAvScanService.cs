using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;

namespace SignFlow.Infrastructure.Services.Security;

/// <summary>Section "Security:ClamAv".</summary>
public class ClamAvOptions
{
    /// <summary>clamd host; empty means no scanner is configured.</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// With no scanner configured, refuse uploads (true, the default) or accept them with only the file-type check
    /// (false: developer machines only, set in appsettings.Development.json).
    /// </summary>
    public bool Required { get; set; } = true;
}

/// <summary>
/// Scans uploads with ClamAV over clamd's INSTREAM protocol (R-M2-2): the file is streamed in chunks and clamd
/// answers "stream: OK" or "stream: &lt;name&gt; FOUND". A scanner that cannot be reached refuses the upload.
/// </summary>
public class ClamAvScanService(IOptions<ClamAvOptions> options, ILogger<ClamAvScanService> logger) : IVirusScanService
{
    private const int ChunkSize = 64 * 1024;

    public async Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        string reply;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(settings.Host!, settings.Port, timeout.Token);
            await using var stream = client.GetStream();

            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);
            var buffer = new byte[ChunkSize];
            var length = new byte[4];
            int read;
            while ((read = await content.ReadAsync(buffer, timeout.Token)) > 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(length, (uint)read);
                await stream.WriteAsync(length, timeout.Token);
                await stream.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
            await stream.WriteAsync(new byte[4], timeout.Token); // zero-length chunk ends the stream

            using var answer = new MemoryStream();
            await stream.CopyToAsync(answer, timeout.Token);
            reply = Encoding.ASCII.GetString(answer.ToArray()).TrimEnd('\0', '\n', ' ');
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Virus scanner {Host}:{Port} unreachable; upload {FileName} refused", settings.Host, settings.Port, fileName);
            throw new ScannerUnavailableException("The virus scanner is unavailable. Try again in a few minutes.");
        }

        if (reply.EndsWith(" OK", StringComparison.Ordinal))
            return new ScanResult(true);
        if (reply.EndsWith(" FOUND", StringComparison.Ordinal))
            return new ScanResult(false, reply["stream: ".Length..^" FOUND".Length]);

        logger.LogError("Virus scanner answered {Reply} for {FileName}; upload refused", reply, fileName);
        throw new ScannerUnavailableException("The virus scanner could not check the file. Try again in a few minutes.");
    }
}

/// <summary>Stands in when no scanner is configured and one is required: every upload is refused, visibly.</summary>
public class MissingScannerService : IVirusScanService
{
    public Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default) =>
        throw new ScannerUnavailableException("No virus scanner is configured on this server, so uploads are refused.");
}
