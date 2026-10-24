using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;

namespace SignFlow.Infrastructure.Services.Pdf;

public class HtmlTemplatePdfGenerator(
    IStorageService storage,
    ILogger<HtmlTemplatePdfGenerator> logger) : IHtmlTemplatePdfGenerator
{
    public async Task<Stream> GenerateAsync(
        string renderedHtmlContent,
        string title,
        List<HtmlTemplatePdfSignature> signatures,
        CancellationToken ct = default)
    {
        // A signature with a template key goes where the template placed its placeholder; whatever is left over
        // (no placeholder in the text, or an older contract without keys) is appended in a section at the end.
        var inlineBlocks = new Dictionary<string, string>();
        foreach (var sig in signatures.Where(s => !string.IsNullOrEmpty(s.TemplateKey)))
            inlineBlocks[sig.TemplateKey!] = await BuildSignatureRowAsync(sig, ct);

        var (placedHtml, placedKeys) = HtmlSignatureSlots.Apply(renderedHtmlContent, inlineBlocks);
        renderedHtmlContent = placedHtml;

        var sigBlock = await BuildSignatureBlockAsync(
            signatures.Where(s => string.IsNullOrEmpty(s.TemplateKey) || !placedKeys.Contains(s.TemplateKey)).ToList(), ct);

        // $$""" — two-dollar raw string: CSS uses single { } as literals; interpolations use {{ }}
        var fullHtml = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8"/>
              <style>
                {{ContractTheme.Css}}
              </style>
              <style>
                * { box-sizing: border-box; }
                body { margin: 0; font-family: Arial, "Liberation Sans", sans-serif; font-size: 13px;
                       color: #1f1f1f; padding: 0 12px; line-height: 1.6; }
                .template-body { margin-bottom: 48px; font-size: 14px; }
                .sig-section { border-top: 2px solid #0b57d0; padding-top: 24px; margin-top: 32px; }
                .sig-section h3 { font-size: 11px; font-weight: 700; color: #0b57d0;
                                  letter-spacing: 1px; text-transform: uppercase; margin-bottom: 16px; }
                .sig-row { display: flex; align-items: flex-start; gap: 24px; margin-bottom: 16px; }
                .sig-img { border: 1px solid #c7d0dc; border-radius: 6px; padding: 4px;
                           max-width: 220px; max-height: 90px; object-fit: contain; }
                .sig-meta { font-size: 12px; color: #5f6368; }
                .sig-meta strong { display: block; color: #1f1f1f; font-size: 13px; margin-bottom: 2px; }
                .footer { margin-top: 48px; padding-top: 12px; border-top: 1px solid #e8edf2;
                          font-size: 10px; color: #9aa0a6; text-align: center; }
              </style>
            </head>
            <body>
              <div class="template-body contract-doc">{{renderedHtmlContent}}</div>
              {{sigBlock}}
            </body>
            </html>
            """;

        return await RenderToPdfAsync(fullHtml, ct);
    }

    private async Task<string> BuildSignatureBlockAsync(
        List<HtmlTemplatePdfSignature> signatures, CancellationToken ct)
    {
        if (signatures.Count == 0) return string.Empty;

        var rows = new System.Text.StringBuilder();
        foreach (var sig in signatures)
            rows.Append(await BuildSignatureRowAsync(sig, ct));

        return $"""<div class="sig-section"><h3>Signatures</h3>{rows}</div>""";
    }

    private async Task<string> BuildSignatureRowAsync(HtmlTemplatePdfSignature sig, CancellationToken ct)
    {
        string imgTag;
        try
        {
            var imgStream = await storage.DownloadAsync(sig.ImageStoragePath, ct);
            using var ms = new MemoryStream();
            await imgStream.CopyToAsync(ms, ct);
            var b64 = Convert.ToBase64String(ms.ToArray());
            imgTag = $"""<img class="sig-img" src="data:image/png;base64,{b64}" alt="Signature"/>""";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load signature image from {Path}", sig.ImageStoragePath);
            imgTag = """<div style="width:220px;height:60px;border:1px solid #ccc;display:flex;align-items:center;justify-content:center;color:#9aa0a6;font-size:11px;">[signature]</div>""";
        }

        return $"""
            <div class="sig-row">
              {imgTag}
              <div class="sig-meta">
                <strong>{System.Net.WebUtility.HtmlEncode(sig.SignerName)}</strong>
                Signed: {sig.SignedAt:yyyy-MM-dd HH:mm} UTC
              </div>
            </div>
            """;
    }

    private static readonly string[] ChromeArgs =
    [
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
        "--disable-extensions",
        "--run-all-compositor-stages-before-draw"
    ];

    private static async Task<Stream> RenderToPdfAsync(string html, CancellationToken ct)
    {
        var launchOptions = await BuildLaunchOptionsAsync();

        await using var browser = await Puppeteer.LaunchAsync(launchOptions);
        await using var page = await browser.NewPageAsync();

        await page.SetContentAsync(html, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.Networkidle0],
            Timeout = 30_000
        });

        // Puppeteer footers are drawn outside the page body, so they carry their own inline styles.
        var footer = $"""
            <div style="width:100%;font-family:Arial,'Liberation Sans',sans-serif;font-size:8px;color:#9aa0a6;
                        padding:0 15mm;display:flex;justify-content:space-between;">
              <span>Generated by SignFlow &middot; {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</span>
              <span>Page <span class="pageNumber"></span> / <span class="totalPages"></span></span>
            </div>
            """;

        var pdfBytes = await page.PdfDataAsync(new PdfOptions
        {
            Format = PaperFormat.A4,
            PrintBackground = true,
            DisplayHeaderFooter = true,
            HeaderTemplate = "<span></span>",
            FooterTemplate = footer,
            MarginOptions = new MarginOptions
            {
                Top = "20mm", Bottom = "20mm",
                Left = "15mm", Right = "15mm"
            }
        });

        return new MemoryStream(pdfBytes);
    }

    private static async Task<LaunchOptions> BuildLaunchOptionsAsync()
    {
        // On Linux/OpenShift: CHROME_EXECUTABLE_PATH=/usr/bin/chromium (set in Dockerfile)
        var execPath = Environment.GetEnvironmentVariable("CHROME_EXECUTABLE_PATH");
        if (!string.IsNullOrEmpty(execPath))
            return new LaunchOptions { Headless = true, ExecutablePath = execPath, Args = ChromeArgs };

        // On Windows dev: download Chrome automatically if not already present
        var fetcher = new BrowserFetcher();
        var installed = await fetcher.DownloadAsync();
        return new LaunchOptions { Headless = true, ExecutablePath = installed.GetExecutablePath(), Args = ChromeArgs };
    }
}
