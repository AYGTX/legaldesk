using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Infrastructure.Services.Seal;

public class SealSettings
{
    /// <summary>PKCS#12 file of the organisation's seal certificate (with private key), and its password. One per
    /// deployment for now; an organisation-specific file can be named under Organisations:{id}.</summary>
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
    public Dictionary<string, SealCertificateFile> Organisations { get; set; } = [];

    /// <summary>RFC 3161 time-stamping authority. Production: a qualified TSA (TunTrust); DigiCert's public one by default.</summary>
    public string TimestampUrl { get; set; } = "http://timestamp.digicert.com";

    /// <summary>In Development only, a self-signed test seal is created when no certificate is configured.</summary>
    public bool AllowTestCertificate { get; set; } = true;
}

public class SealCertificateFile
{
    public string Path { get; set; } = string.Empty;
    public string? Password { get; set; }
}

public interface ISealCertificateStore
{
    /// <summary>The organisation's seal certificate, with its private key. The caller disposes it.</summary>
    Task<X509Certificate2> GetAsync(Guid organisationId, string organisationName, CancellationToken ct = default);
}

/// <summary>
/// Where the seal certificate comes from: the organisation's own file, else the deployment's, else (Development only) a
/// self-signed test seal kept under App_Data so contracts sealed in testing all verify against the same certificate.
/// </summary>
public class SealCertificateStore(IOptions<SealSettings> options, IWebHostEnvironment environment, ILogger<SealCertificateStore> logger)
    : ISealCertificateStore
{
    private static readonly SemaphoreSlim CreateLock = new(1, 1);

    public async Task<X509Certificate2> GetAsync(Guid organisationId, string organisationName, CancellationToken ct = default)
    {
        var settings = options.Value;
        if (settings.Organisations.TryGetValue(organisationId.ToString(), out var own) && File.Exists(own.Path))
            return Load(own.Path, own.Password);
        if (!string.IsNullOrWhiteSpace(settings.CertificatePath))
            return Load(settings.CertificatePath, settings.CertificatePassword);

        if (!environment.IsDevelopment() || !settings.AllowTestCertificate)
            throw new InvalidOperationException("No seal certificate is configured (Seal:CertificatePath).");

        var folder = Path.Combine(environment.ContentRootPath, "App_Data", "seal");
        var pfx = Path.Combine(folder, $"{organisationId:N}.pfx");
        var passwordFile = pfx + ".password";
        await CreateLock.WaitAsync(ct);
        try
        {
            if (!File.Exists(pfx))
            {
                Directory.CreateDirectory(folder);
                var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                using var created = CreateTestSeal(organisationName);
                await File.WriteAllBytesAsync(pfx, created.Export(X509ContentType.Pfx, password), ct);
                await File.WriteAllTextAsync(passwordFile, password, ct);
                logger.LogWarning("Created a self-signed TEST seal for {Organisation} at {Path}: not for production", organisationName, pfx);
            }
            return Load(pfx, await File.ReadAllTextAsync(passwordFile, ct));
        }
        finally
        {
            CreateLock.Release();
        }
    }

    private static X509Certificate2 Load(string path, string? password) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);

    /// <summary>An RSA-3072 self-signed certificate marked for document signing, valid 3 years.</summary>
    public static X509Certificate2 CreateTestSeal(string organisationName)
    {
        using var key = RSA.Create(3072);
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN={Escape(organisationName)} (LegalDesk TEST seal), O={Escape(organisationName)}"),
            key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.4.1.311.10.3.12")], false)); // document signing
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(3));
    }

    private static string Escape(string value) => value.Replace(",", "\\,").Replace("=", "\\=");
}

/// <summary>RFC 3161 over HTTP: SHA-256 imprint, a nonce, the TSA's certificate requested, the answer checked.</summary>
public class Rfc3161TimestampAuthority(IHttpClientFactory httpFactory, IOptions<SealSettings> options) : ITimestampAuthority
{
    public const string HttpClientName = "tsa";

    public async Task<byte[]> TimestampAsync(byte[] sha256, CancellationToken ct = default)
    {
        var request = Rfc3161TimestampRequest.CreateFromHash(sha256, HashAlgorithmName.SHA256,
            nonce: RandomNumberGenerator.GetBytes(8), requestSignerCertificates: true);

        using var content = new ByteArrayContent(request.Encode());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
        using var client = httpFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsync(options.Value.TimestampUrl, content, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsByteArrayAsync(ct);

        // Checks status, imprint and nonce; throws CryptographicException when the answer is not for this request.
        var token = request.ProcessResponse(body, out _);
        return token.AsSignedCms().Encode();
    }
}
