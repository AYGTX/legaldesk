using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Services;

namespace SignFlow.Infrastructure.Services.Pdf;

public class OfficeSettings
{
    /// <summary>The LibreOffice executable: "soffice" on the path, or its full path on Windows.</summary>
    public string SofficePath { get; set; } = "soffice";
    public int TimeoutSeconds { get; set; } = 90;
}

/// <summary>
/// Turns an edited Word file into the PDF of the next version (R-M7-3) with LibreOffice running headless on this server;
/// the contract never leaves it. Each conversion gets its own profile folder, so two conversions never share a lock and a
/// crashed one leaves nothing behind for the next.
/// </summary>
public class LibreOfficeDocxConverter(IOptions<OfficeSettings> options, ILogger<LibreOfficeDocxConverter> logger) : IDocxConverter
{
    public async Task<Stream> ToPdfAsync(Stream docx, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var dir = Directory.CreateTempSubdirectory("legaldesk-docx-");
        try
        {
            var input = Path.Combine(dir.FullName, "contract.docx");
            await using (var file = File.Create(input))
                await docx.CopyToAsync(file, cancellationToken);

            var profile = new Uri(Path.Combine(dir.FullName, "profile")).AbsoluteUri;
            var start = new ProcessStartInfo(settings.SofficePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
                     {
                         $"-env:UserInstallation={profile}", "--headless", "--norestore", "--nologo", "--nodefault",
                         "--convert-to", "pdf", "--outdir", dir.FullName, input
                     })
                start.ArgumentList.Add(argument);

            Process process;
            try
            {
                process = Process.Start(start) ?? throw new InvalidOperationException("soffice did not start.");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                logger.LogWarning("Word revisions are off: LibreOffice is not installed ({Message})", ex.Message);
                throw new ConflictException("revision.docx_unavailable",
                    "Word files cannot be converted on this server. Upload the revision as a PDF.");
            }

            using (process)
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                limit.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
                var error = process.StandardError.ReadToEndAsync(limit.Token);
                _ = process.StandardOutput.ReadToEndAsync(limit.Token);
                try
                {
                    await process.WaitForExitAsync(limit.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                    throw new ConflictException("revision.docx_failed", "The Word file took too long to convert.");
                }

                var output = Path.Combine(dir.FullName, "contract.pdf");
                if (process.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
                {
                    logger.LogWarning("LibreOffice could not convert a Word revision ({Code}): {Error}", process.ExitCode, (await error).Trim());
                    throw new ConflictException("revision.docx_failed", "This Word file could not be read. Save it again in Word, or upload a PDF.");
                }

                return new MemoryStream(await File.ReadAllBytesAsync(output, cancellationToken));
            }
        }
        finally
        {
            try { dir.Delete(recursive: true); }
            catch (IOException ex) { logger.LogWarning(ex, "Could not delete the conversion folder {Folder}", dir.FullName); }
        }
    }
}
