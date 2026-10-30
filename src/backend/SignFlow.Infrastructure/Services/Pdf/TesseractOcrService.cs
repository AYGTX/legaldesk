using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Infrastructure.Services.Pdf;

public class OcrSettings
{
    public bool Enabled { get; set; } = true;
    public string TesseractPath { get; set; } = "tesseract";
    public string PdftoppmPath { get; set; } = "pdftoppm";
    /// <summary>Tesseract language packs, joined by '+'.</summary>
    public string Languages { get; set; } = "fra+eng+ara";
    public int Dpi { get; set; } = 300;
    public int PageTimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Text recognition with the Tesseract and pdftoppm command-line tools (R-M2-4): pdftoppm draws each page, Tesseract
/// reads it in French, English and Arabic. Both run on this server; the contract never leaves it. When either tool is
/// missing, <see cref="IsAvailable"/> is false and a scanned contract is marked unreadable rather than reviewed empty.
/// </summary>
public class TesseractOcrService(IOptions<OcrSettings> options, ILogger<TesseractOcrService> logger) : IOcrService
{
    private readonly Lazy<bool> _available = new(() => Probe(options.Value, logger));

    public bool IsAvailable => options.Value.Enabled && _available.Value;

    public async Task<string> RecognizeAsync(Stream pdf, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var dir = Directory.CreateTempSubdirectory("legaldesk-ocr-");
        try
        {
            var input = Path.Combine(dir.FullName, "contract.pdf");
            await using (var file = File.Create(input))
                await pdf.CopyToAsync(file, cancellationToken);

            var pages = Path.Combine(dir.FullName, "page");
            await RunAsync(settings.PdftoppmPath, ["-r", settings.Dpi.ToString(), "-png", input, pages],
                TimeSpan.FromSeconds(settings.PageTimeoutSeconds * 10), cancellationToken);

            // pdftoppm numbers pages with zero padding that depends on the page count: sort by the number, not the name.
            var images = dir.GetFiles("page-*.png")
                .OrderBy(f => int.TryParse(Path.GetFileNameWithoutExtension(f.Name)[5..], out var n) ? n : int.MaxValue)
                .ToList();

            var text = new StringBuilder();
            foreach (var image in images)
            {
                var pageText = await RunAsync(settings.TesseractPath, [image.FullName, "stdout", "-l", settings.Languages, "--psm", "3"],
                    TimeSpan.FromSeconds(settings.PageTimeoutSeconds), cancellationToken);
                if (text.Length > 0) text.Append("\n\n");
                text.Append(pageText.Trim());
            }
            return text.ToString();
        }
        finally
        {
            try { dir.Delete(recursive: true); }
            catch (IOException ex) { logger.LogWarning(ex, "Could not delete the OCR work folder {Folder}", dir.FullName); }
        }
    }

    private static async Task<string> RunAsync(string tool, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        var start = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{tool} did not start.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);

        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var error = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(tool)} failed ({process.ExitCode}): {(await error).Trim()}");
        return await output;
    }

    /// <summary>Both tools answer. Checked once, at the first scanned contract.</summary>
    private static bool Probe(OcrSettings settings, ILogger logger)
    {
        if (!settings.Enabled) return false;
        foreach (var (tool, argument) in new[] { (settings.TesseractPath, "--version"), (settings.PdftoppmPath, "-v") })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(tool, argument)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
                });
                if (process is null || !process.WaitForExit(10_000))
                {
                    logger.LogWarning("Text recognition is off: {Tool} did not answer", tool);
                    return false;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                logger.LogWarning("Text recognition is off: {Tool} is not installed ({Message})", tool, ex.Message);
                return false;
            }
        }
        return true;
    }
}
