using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Features.Documents.Commands;
using SignFlow.Application.Features.Documents.Queries;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;
using SignFlow.Infrastructure.Services.Pdf;
using SignFlow.Infrastructure.Services.Seal;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// R-M5-8 and R-M5-9: a completed contract gets an evidence page and the organisation's PAdES-B-T seal (CAdES detached
/// signature with the signing-certificate-v2 attribute and an RFC 3161 timestamp over the signature); its SHA-256 is
/// stored, and the public check says whether an uploaded file is the sealed one. The timestamp authority here is a
/// local test one that signs real RFC 3161 tokens.
/// </summary>
public class SealTests
{
    static SealTests()
    {
        PdfSharp.Fonts.GlobalFontSettings.FontResolver ??= new AppFontResolver();
    }

    private sealed class TestCertificates : ISealCertificateStore
    {
        private static readonly X509Certificate2 Seal = SealCertificateStore.CreateTestSeal("Consultim IT");
        public Task<X509Certificate2> GetAsync(Guid organisationId, string organisationName, CancellationToken ct = default) =>
            Task.FromResult(X509CertificateLoader.LoadPkcs12(Seal.Export(X509ContentType.Pfx), null));
    }

    /// <summary>A real RFC 3161 token: TSTInfo signed by a time-stamping certificate, with ESSCertIDv2.</summary>
    private sealed class TestTimestampAuthority : ITimestampAuthority
    {
        public static readonly X509Certificate2 Tsa = CreateTsa();
        public List<byte[]> Imprints { get; } = [];

        public Task<byte[]> TimestampAsync(byte[] sha256, CancellationToken ct = default)
        {
            Imprints.Add(sha256);
            var info = new Rfc3161TimestampTokenInfo(new Oid("1.2.3.4.5"), new Oid("2.16.840.1.101.3.4.2.1"), sha256,
                new BigInteger(RandomNumberGenerator.GetBytes(8), isUnsigned: true).ToByteArray(), DateTimeOffset.UtcNow);
            var cms = new SignedCms(new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), info.Encode()));
            var signer = new CmsSigner(Tsa) { DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), IncludeOption = X509IncludeOption.EndCertOnly };
            signer.SignedAttributes.Add(new AsnEncodedData(CadesSignature.SigningCertificateV2Oid, CadesSignature.SigningCertificateV2(Tsa)));
            cms.ComputeSignature(signer);
            return Task.FromResult(cms.Encode());
        }

        private static X509Certificate2 CreateTsa()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Test TSA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.8")], critical: true));
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
            return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        }
    }

    private static byte[] OnePagePdf()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("Contrat de prestation", new XFont(AppFontResolver.FamilyName, 14), XBrushes.Black, new XPoint(60, 80));
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static (int, int, int, int) ByteRange(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var open = text.IndexOf('[', text.IndexOf("/ByteRange", StringComparison.Ordinal));
        var parts = text[(open + 1)..text.IndexOf(']', open)].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        return (parts[0], parts[1], parts[2], parts[3]);
    }

    [Fact]
    public async Task The_seal_is_a_cades_detached_signature_over_the_whole_file_with_a_timestamp_on_the_signature()
    {
        var tsa = new TestTimestampAuthority();
        var sealedPdf = await new PdfSealer(new TestCertificates(), tsa).SealAsync(OnePagePdf(), Guid.NewGuid(), "Consultim IT", "Signed by all parties");

        var text = Encoding.Latin1.GetString(sealedPdf);
        Assert.Contains("/SubFilter /ETSI.CAdES.detached", text.Replace("/SubFilter/", "/SubFilter /"));
        Assert.DoesNotContain("adbe.pkcs7.detached", text);

        // The byte range starts at 0, ends at the end of the file, and skips only the signature.
        var (a, b, c, d) = ByteRange(sealedPdf);
        Assert.Equal(0, a);
        Assert.Equal(sealedPdf.Length, c + d);
        Assert.Equal((byte)'<', sealedPdf[b]);
        Assert.Equal((byte)'>', sealedPdf[c - 1]);

        var hex = text[(b + 1)..(c - 1)].TrimEnd('0');
        if (hex.Length % 2 == 1) hex += "0";
        var cms = new SignedCms(new ContentInfo([.. sealedPdf[a..(a + b)], .. sealedPdf[c..(c + d)]]), detached: true);
        cms.Decode(Convert.FromHexString(hex));
        cms.CheckSignature(verifySignatureOnly: true);   // the seal is valid over the byte range

        var signer = cms.SignerInfos[0];
        Assert.Equal("2.16.840.1.101.3.4.2.1", signer.DigestAlgorithm.Value);
        Assert.Contains(signer.SignedAttributes.Cast<CryptographicAttributeObject>(), x => x.Oid.Value == CadesSignature.SigningCertificateV2Oid);
        Assert.DoesNotContain(signer.SignedAttributes.Cast<CryptographicAttributeObject>(), x => x.Oid.Value == "1.2.840.113549.1.9.5"); // no signing-time

        // Level T: the timestamp token is over the signature value, and decodes as RFC 3161.
        var tokenAttribute = signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Single(x => x.Oid.Value == CadesSignature.SignatureTimestampOid);
        Assert.True(Rfc3161TimestampToken.TryDecode(tokenAttribute.Values[0].RawData, out var token, out _));
        Assert.Equal(SHA256.HashData(signer.GetSignature()), token!.TokenInfo.GetMessageHash().ToArray());
        Assert.Equal(SHA256.HashData(signer.GetSignature()), Assert.Single(tsa.Imprints));
    }

    [Fact]
    public async Task A_completed_contract_gets_its_evidence_page_and_seal_and_the_public_check_finds_one_changed_byte()
    {
        using var fixture = new ScenarioFixture(Guid.NewGuid());
        var db = (ApplicationDbContext)fixture.Db;
        var storage = new InMemoryStorageService();
        var original = OnePagePdf();
        var path = await storage.UploadAsync(new MemoryStream(original), "c.pdf", "application/pdf", "documents");

        db.Organisations.Add(Organisation.Create("Consultim IT", "consultim-" + Guid.NewGuid().ToString("N")[..6], fixture.OrganisationId));
        var document = Document.Create("Supply agreement", "c.pdf", path, original.Length, 1, Guid.NewGuid());
        document.SetHash(Convert.ToHexString(SHA256.HashData(original)));
        document.SeedStatus(DocumentStatus.Completed);
        db.Documents.Add(document);
        var signer = Recipient.Create(document.Id, "Alice", "alice@partner.com", 1, ApprovalType.Signature);
        signer.MarkVerified("EmailCode");
        signer.MarkSigned("203.0.113.7", "test");
        db.Recipients.Add(signer);
        await db.SaveChangesAsync();

        var sealer = new DocumentSealer(fixture.Db, storage, new EvidencePageWriter(),
            new PdfSealer(new TestCertificates(), new TestTimestampAuthority()), fixture.GetHandler<IAuditService>(),
            Options.Create(new SecurityOptions { PublicUrl = "https://legaldesk.example" }), NullLogger<DocumentSealer>.Instance);
        await sealer.SealAsync(document.Id);

        var stored = await fixture.Db.Documents.SingleAsync();
        Assert.NotNull(stored.SealedAt);
        var sealedPdf = storage.Files[stored.FinalStoragePath!];
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(sealedPdf)), stored.FinalSha256);
        Assert.Contains("ETSI.CAdES.detached", Encoding.Latin1.GetString(sealedPdf));
        Assert.Contains((fixture.GetHandler<IAuditService>() as NullAuditService)!.Entries, e => e.Action == AuditAction.DocumentSealed);

        // The evidence page is a second page; sealing again does nothing.
        using (var reread = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(sealedPdf), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            Assert.Equal(2, reread.PageCount);
        await sealer.SealAsync(document.Id);
        Assert.Equal(stored.FinalSha256, (await fixture.Db.Documents.SingleAsync()).FinalSha256);

        // The public check: the sealed file matches; the same file with one byte changed does not.
        var platform = new FakeSeals(new SealRecord(stored.VerificationCode!, "Consultim IT", stored.SealedAt!.Value, stored.FinalSha256!));
        var check = new CheckSealedFileCommandHandler(platform);
        Assert.True((await check.HandleAsync(new CheckSealedFileCommand(stored.VerificationCode!.ToLowerInvariant(), new MemoryStream(sealedPdf)))).Matches);
        var tampered = (byte[])sealedPdf.Clone();
        tampered[tampered.Length / 2] ^= 0x01;
        Assert.False((await check.HandleAsync(new CheckSealedFileCommand(stored.VerificationCode!, new MemoryStream(tampered)))).Matches);
        Assert.Equal("Consultim IT", (await new VerifySealQueryHandler(platform).HandleAsync(new VerifySealQuery(stored.VerificationCode!))).OrganisationName);
    }

    private sealed class FakeSeals(SealRecord seal) : IPlatformQueries
    {
        public Task<SealRecord?> FindSealAsync(string code, CancellationToken ct = default) => Task.FromResult(code == seal.Code ? seal : null);
        public Task<Guid?> OrganisationOfUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task<(Guid UserId, Guid OrganisationId)?> UserOfCalendarTokenAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult<(Guid UserId, Guid OrganisationId)?>(null);
        public Task<Guid?> OrganisationOfSigningTokenAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task<Guid?> OrganisationOfSigningSessionAsync(string secretHash, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task<IReadOnlyList<Guid>> ActiveOrganisationIdsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<Guid?> SoleOrganisationIdAsync(CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task<bool> EmailInUseAsync(string email, CancellationToken ct = default) => Task.FromResult(false);
    }
}
