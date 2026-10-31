using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace SignFlow.Infrastructure.Services.Pdf;

public class PdfRenderingSettings
{
    /// <summary>
    /// Chrome's own sandbox (R-M6-4). On by default. A host whose kernel forbids it (some containers without user
    /// namespaces) can turn it off; JavaScript stays off and the network stays blocked either way.
    /// </summary>
    public bool ChromeSandbox { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
    /// <summary>Pages rendered at the same time by this server's one browser.</summary>
    public int MaxConcurrentPages { get; set; } = 4;
}

/// <summary>
/// Turns contract HTML into PDF (R-M6-4) with one long-lived headless Chrome per server, never one per document. Each
/// page runs with JavaScript off, and every request it makes is refused except the contract document itself: images
/// travel as data: URIs and fonts are the server's own, so a template cannot make the server fetch anything — not an
/// internal address, not an external image. A render that takes longer than the limit is abandoned.
/// </summary>
public sealed class ChromeRenderer(IOptions<PdfRenderingSettings> options, ILogger<ChromeRenderer> logger) : IAsyncDisposable
{
    /// <summary>The address the contract is served from inside the browser; nothing is ever fetched from it.</summary>
    private const string DocumentUrl = "https://contract.legaldesk.invalid/";

    private readonly SemaphoreSlim _launch = new(1, 1);
    private readonly SemaphoreSlim _pages = new(Math.Max(1, options.Value.MaxConcurrentPages));
    private IBrowser? _browser;

    public async Task<Stream> RenderAsync(string html, CancellationToken ct = default)
    {
        var settings = options.Value;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        await _pages.WaitAsync(limit.Token);
        try
        {
            var browser = await BrowserAsync(limit.Token);
            await using var page = await browser.NewPageAsync();
            page.DefaultTimeout = settings.TimeoutSeconds * 1000;
            page.DefaultNavigationTimeout = settings.TimeoutSeconds * 1000;

            await page.SetJavaScriptEnabledAsync(false);
            await page.SetRequestInterceptionAsync(true);
            var blocked = 0;
            page.Request += async (_, e) =>
            {
                try
                {
                    if (e.Request.Url == DocumentUrl && e.Request.ResourceType == ResourceType.Document)
                        await e.Request.RespondAsync(new ResponseData { Status = System.Net.HttpStatusCode.OK, ContentType = "text/html; charset=utf-8", Body = html });
                    else
                    {
                        Interlocked.Increment(ref blocked);
                        await e.Request.AbortAsync(RequestAbortErrorCode.BlockedByClient);
                    }
                }
                catch (Exception ex) when (ex is PuppeteerException or InvalidOperationException)
                {
                    // The page closed while a request was pending: nothing left to answer.
                }
            };

            await page.GoToAsync(DocumentUrl, new NavigationOptions { WaitUntil = [WaitUntilNavigation.Load] }).WaitAsync(limit.Token);
            if (blocked > 0)
                logger.LogWarning("Contract rendering blocked {Count} outgoing request(s) from the contract HTML", blocked);

            var pdf = await page.PdfDataAsync(PdfOptionsFor(DateTime.UtcNow)).WaitAsync(limit.Token);
            return new MemoryStream(pdf);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The contract could not be rendered within {settings.TimeoutSeconds} s.");
        }
        finally
        {
            _pages.Release();
        }
    }

    private static PdfOptions PdfOptionsFor(DateTime now) => new()
    {
        Format = PaperFormat.A4,
        PrintBackground = true,
        DisplayHeaderFooter = true,
        HeaderTemplate = "<span></span>",
        // Footers are drawn outside the page body, so they carry their own inline styles.
        FooterTemplate = $"""
            <div style="width:100%;font-family:Arial,'Liberation Sans',sans-serif;font-size:8px;color:#9aa0a6;
                        padding:0 15mm;display:flex;justify-content:space-between;">
              <span>LegalDesk &middot; {now:yyyy-MM-dd HH:mm} UTC</span>
              <span>Page <span class="pageNumber"></span> / <span class="totalPages"></span></span>
            </div>
            """,
        MarginOptions = new MarginOptions { Top = "20mm", Bottom = "20mm", Left = "15mm", Right = "15mm" }
    };

    /// <summary>The server's browser, started on first use and again if it went away.</summary>
    private async Task<IBrowser> BrowserAsync(CancellationToken ct)
    {
        if (_browser is { IsConnected: true } running) return running;
        await _launch.WaitAsync(ct);
        try
        {
            if (_browser is { IsConnected: true } started) return started;
            if (_browser is not null)
            {
                logger.LogWarning("The rendering browser went away; starting a new one");
                try { await _browser.DisposeAsync(); } catch (Exception ex) when (ex is PuppeteerException or InvalidOperationException) { }
            }
            _browser = await Puppeteer.LaunchAsync(await LaunchOptionsAsync());
            return _browser;
        }
        finally
        {
            _launch.Release();
        }
    }

    private async Task<LaunchOptions> LaunchOptionsAsync()
    {
        var args = new List<string>
        {
            "--disable-dev-shm-usage", "--disable-gpu", "--disable-extensions", "--disable-background-networking",
            "--disable-sync", "--disable-default-apps", "--no-first-run", "--mute-audio", "--disable-component-update",
            "--run-all-compositor-stages-before-draw",
            // Refusing a request is not enough: Chrome opens connections ahead of requests (preconnect). Every
            // connection, loopback included, goes to a proxy that does not exist, so no socket reaches a real address.
            "--proxy-server=127.0.0.1:9", "--proxy-bypass-list=<-loopback>",
            "--disable-features=NetworkPrediction,PreconnectToSearch", "--dns-prefetch-disable", "--no-pings"
        };
        if (!options.Value.ChromeSandbox)
        {
            logger.LogWarning("Chrome's sandbox is off (Pdf:ChromeSandbox=false); JavaScript stays off and the network blocked");
            args.AddRange(["--no-sandbox", "--disable-setuid-sandbox"]);
        }

        // In the container: CHROME_EXECUTABLE_PATH=/usr/bin/chromium (set in the Dockerfile).
        var execPath = Environment.GetEnvironmentVariable("CHROME_EXECUTABLE_PATH");
        if (string.IsNullOrEmpty(execPath))
        {
            // On a developer machine: download Chrome once if it is not there.
            var installed = await new BrowserFetcher().DownloadAsync();
            execPath = installed.GetExecutablePath();
        }
        return new LaunchOptions { Headless = true, ExecutablePath = execPath, Args = [.. args] };
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _launch.Dispose();
        _pages.Dispose();
    }
}
