using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Infrastructure.Services.Pdf;
using SignFlow.Infrastructure.Services.Security;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// R-M2-1 and R-M2-2: only real, readable, unencrypted PDFs of at most 50 MB that the scanner passes get in, and a
/// scanner that cannot be reached refuses the upload instead of waving it through.
/// </summary>
public class UploadGuardTests
{
    private sealed class FakeScanner(ScanResult? result = null, bool down = false) : IVirusScanService
    {
        public Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default) =>
            down ? throw new ScannerUnavailableException("down") : Task.FromResult(result ?? new ScanResult(true));
    }

    private static PdfUploadGuard Guard(IVirusScanService? scanner = null) =>
        new(new PdfInspector(), scanner ?? new FakeScanner(), NullLogger<PdfUploadGuard>.Instance);

    private static MemoryStream Pdf(int pages = 2, string? password = null)
    {
        PdfSharp.Fonts.GlobalFontSettings.FontResolver ??= new AppFontResolver();
        using var document = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"Article {i + 1}", new XFont("Arial", 12), XBrushes.Black, new XPoint(72, 72));
        }
        if (password is not null)
            document.SecuritySettings.UserPassword = password;
        var stream = new MemoryStream();
        document.Save(stream, false);
        stream.Position = 0;
        return stream;
    }

    private static async Task<string> Refusal(Stream file, PdfUploadGuard? guard = null)
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() => (guard ?? Guard()).EnsureSafeAsync(file, "contract.pdf"));
        return string.Join(" ", error.Errors.SelectMany(e => e.Value));
    }

    [Fact]
    public async Task A_real_pdf_is_accepted_with_its_page_count_and_the_stream_rewound()
    {
        using var pdf = Pdf(pages: 3);
        Assert.Equal(3, await Guard().EnsureSafeAsync(pdf, "contract.pdf"));
        Assert.Equal(0, pdf.Position);
    }

    [Fact]
    public async Task A_text_file_renamed_pdf_is_refused()
    {
        using var file = new MemoryStream(Encoding.UTF8.GetBytes("Dear client, this is not a PDF."));
        Assert.Contains("not a PDF", await Refusal(file));
    }

    [Fact]
    public async Task A_damaged_pdf_is_refused()
    {
        using var file = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> garbage"));
        Assert.Contains("damaged", await Refusal(file));
    }

    [Fact]
    public async Task A_password_protected_pdf_is_refused()
    {
        using var pdf = Pdf(password: "secret");
        Assert.Contains("password-protected", await Refusal(pdf));
    }

    [Fact]
    public async Task A_file_over_50_mb_is_refused()
    {
        using var big = new MemoryStream(new byte[PdfUploadGuard.MaxBytes + 1]);
        Assert.Contains("50 MB", await Refusal(big));
    }

    [Fact]
    public async Task A_file_the_scanner_flags_is_refused()
    {
        using var pdf = Pdf();
        Assert.Contains("virus scanner", await Refusal(pdf, Guard(new FakeScanner(new ScanResult(false, "Eicar-Signature")))));
    }

    [Fact]
    public async Task An_unreachable_scanner_refuses_the_upload()
    {
        using var pdf = Pdf();
        await Assert.ThrowsAsync<ScannerUnavailableException>(() => Guard(new FakeScanner(down: true)).EnsureSafeAsync(pdf, "c.pdf"));
        await Assert.ThrowsAsync<ScannerUnavailableException>(() => new MissingScannerService().ScanAsync(pdf, "c.pdf"));
    }

    // ------------------------------------------------------------------ clamd protocol

    [Theory]
    [InlineData("stream: OK\0", true, null)]
    [InlineData("stream: Eicar-Signature FOUND\0", false, "Eicar-Signature")]
    public async Task ClamAv_sends_the_file_in_chunks_and_reads_the_verdict(string reply, bool safe, string? threat)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var payload = Enumerable.Range(0, 150_000).Select(i => (byte)(i % 251)).ToArray();

        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var command = new byte[10];
            await stream.ReadExactlyAsync(command);
            Assert.Equal("zINSTREAM\0", Encoding.ASCII.GetString(command));

            var received = new MemoryStream();
            var length = new byte[4];
            while (true)
            {
                await stream.ReadExactlyAsync(length);
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(length);
                if (size == 0) break;
                var chunk = new byte[size];
                await stream.ReadExactlyAsync(chunk);
                received.Write(chunk);
            }
            await stream.WriteAsync(Encoding.ASCII.GetBytes(reply));
            client.Client.Shutdown(SocketShutdown.Send);
            return received.ToArray();
        });

        var scanner = new ClamAvScanService(Options.Create(new ClamAvOptions { Host = "127.0.0.1", Port = port }),
            NullLogger<ClamAvScanService>.Instance);
        var result = await scanner.ScanAsync(new MemoryStream(payload), "c.pdf");

        Assert.Equal(payload, await server);
        Assert.Equal(safe, result.IsSafe);
        Assert.Equal(threat, result.ThreatName);
        listener.Stop();
    }

    [Fact]
    public async Task ClamAv_that_is_not_listening_refuses_the_upload()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // nothing listens on this port any more

        var scanner = new ClamAvScanService(Options.Create(new ClamAvOptions { Host = "127.0.0.1", Port = port, TimeoutSeconds = 5 }),
            NullLogger<ClamAvScanService>.Instance);
        await Assert.ThrowsAsync<ScannerUnavailableException>(() => scanner.ScanAsync(new MemoryStream([1, 2, 3]), "c.pdf"));
    }
}
