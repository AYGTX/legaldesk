using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignFlow.Infrastructure.Services.Pdf;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// The contract renderer (R-M6-4) with a real headless Chrome: a template cannot make the server fetch anything, and a
/// warm browser renders contracts quickly. Needs Chrome (downloaded on first use on a developer machine).
/// </summary>
[Trait("Category", "Chrome")]
public class ChromeRendererTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static ChromeRenderer Renderer() =>
        new(Options.Create(new PdfRenderingSettings()), NullLogger<ChromeRenderer>.Instance);

    [Fact]
    public async Task A_template_that_points_at_an_address_makes_no_network_call()
    {
        // A local listener stands in for an internal address (169.254.169.254 and the like): it must never be called.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var called = false;
        var accept = Task.Run(async () =>
        {
            try { using var c = await listener.AcceptTcpClientAsync(); called = true; }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        });

        await using var renderer = Renderer();
        var html = $"""
            <html><head><link rel="stylesheet" href="http://127.0.0.1:{port}/x.css"></head><body>
            <p>Contrat</p><img src="http://127.0.0.1:{port}/meta-data">
            <iframe src="http://127.0.0.1:{port}/frame"></iframe>
            <script>fetch('http://127.0.0.1:{port}/js')</script>
            <div style="background:url(http://127.0.0.1:{port}/bg)">x</div>
            </body></html>
            """;

        await using var pdf = await renderer.RenderAsync(html);
        await Task.Delay(300);
        listener.Stop();
        await accept;

        Assert.False(called, "the renderer opened a connection the template asked for");
        var head = new byte[5];
        pdf.Position = 0;
        Assert.Equal(5, await pdf.ReadAsync(head));
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(head));
    }

    [Fact]
    public async Task Twenty_contracts_in_a_row_take_under_twenty_seconds_on_a_warm_browser()
    {
        await using var renderer = Renderer();
        var body = string.Concat(Enumerable.Range(1, 40).Select(i => $"<h2>Article {i}</h2><p>{new string('x', 600)}</p>"));
        await (await renderer.RenderAsync("<p>warm-up</p>")).DisposeAsync();

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            var one = Stopwatch.StartNew();
            await (await renderer.RenderAsync($"<html><body>{body}</body></html>")).DisposeAsync();
            output.WriteLine($"render {i + 1}: {one.ElapsedMilliseconds} ms");
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"20 renders took {clock.Elapsed.TotalSeconds:F1} s");
    }
}
