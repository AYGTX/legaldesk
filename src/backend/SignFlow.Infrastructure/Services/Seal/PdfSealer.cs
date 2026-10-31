using System.Globalization;
using System.Text;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Infrastructure.Services.Seal;

/// <summary>
/// Seals a PDF with the organisation's certificate as PAdES-B-T (R-M5-8). PDFsharp writes the signature dictionary and
/// reserves the space for it; PDFsharp only knows the "adbe.pkcs7.detached" sub-filter, so that name is replaced in
/// place by "ETSI.CAdES.detached" (same length, nothing moves), then the CAdES signature over the byte range is computed
/// here and written into the reserved space.
/// </summary>
public class PdfSealer(ISealCertificateStore certificates, ITimestampAuthority? timestampAuthority) : IPdfSealer
{
    /// <summary>Room for the signature: certificate chain, signed attributes and the timestamp token.</summary>
    public const int ReservedBytes = 24 * 1024;

    private static readonly byte[] PkcsSubFilter = Encoding.ASCII.GetBytes("adbe.pkcs7.detached");
    private static readonly byte[] CadesSubFilter = Encoding.ASCII.GetBytes("ETSI.CAdES.detached");

    public async Task<byte[]> SealAsync(byte[] pdf, Guid organisationId, string organisationName, string reason, CancellationToken ct = default)
    {
        using var certificate = await certificates.GetAsync(organisationId, organisationName, ct);

        // 1. PDFsharp: signature field, dictionary, /ByteRange, and zeros where the signature goes.
        byte[] prepared;
        using (var input = new MemoryStream(pdf))
        using (var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify))
        {
            DigitalSignatureHandler.ForDocument(document, new PlaceholderSigner(certificate.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false)),
                new DigitalSignatureOptions
                {
                    Reason = reason,
                    Location = "LegalDesk",
                    ContactInfo = organisationName,
                    AppName = "LegalDesk",
                    PageIndex = 0
                });
            using var output = new MemoryStream();
            await document.SaveAsync(output);
            prepared = output.ToArray();
        }

        // 2. The PAdES sub-filter, in place.
        var at = IndexOf(prepared, PkcsSubFilter, 0);
        if (at < 0 || IndexOf(prepared, PkcsSubFilter, at + 1) >= 0)
            throw new InvalidOperationException("Expected exactly one signature dictionary in the prepared PDF.");
        CadesSubFilter.CopyTo(prepared, at);

        // 3. The byte range PDFsharp wrote, and the reserved hole between its two parts.
        var (start1, length1, start2, length2) = ReadByteRange(prepared);
        var holeStart = start1 + length1;   // '<'
        var holeEnd = start2;               // just after '>'
        if (prepared[holeStart] != (byte)'<' || prepared[holeEnd - 1] != (byte)'>')
            throw new InvalidOperationException("The signature placeholder is not where /ByteRange says.");

        var signed = new byte[length1 + length2];
        Buffer.BlockCopy(prepared, start1, signed, 0, length1);
        Buffer.BlockCopy(prepared, start2, signed, length1, length2);

        // 4. CAdES signature with its timestamp, written as hex into the hole.
        var cms = await CadesSignature.SignAsync(signed, certificate,
            timestampAuthority is null ? null : (hash, token) => timestampAuthority.TimestampAsync(hash, token), ct);
        var hex = Convert.ToHexString(cms);
        var room = holeEnd - holeStart - 2;
        if (hex.Length > room)
            throw new InvalidOperationException($"The signature ({hex.Length / 2} bytes) does not fit the {room / 2} bytes reserved.");
        Encoding.ASCII.GetBytes(hex.PadRight(room, '0')).CopyTo(prepared, holeStart + 1);
        return prepared;
    }

    private static (int, int, int, int) ReadByteRange(byte[] pdf)
    {
        var key = IndexOf(pdf, "/ByteRange"u8.ToArray(), 0);
        if (key < 0) throw new InvalidOperationException("No /ByteRange in the prepared PDF.");
        var open = Array.IndexOf(pdf, (byte)'[', key);
        var close = Array.IndexOf(pdf, (byte)']', open);
        var numbers = Encoding.ASCII.GetString(pdf, open + 1, close - open - 1)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(n => int.Parse(n, CultureInfo.InvariantCulture))
            .ToArray();
        if (numbers.Length != 4) throw new InvalidOperationException("Unexpected /ByteRange.");
        return (numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        for (var i = from; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length && match; j++)
                match = haystack[i + j] == needle[j];
            if (match) return i;
        }
        return -1;
    }

    /// <summary>Reserves the space; the real signature is written afterwards (step 4).</summary>
    private sealed class PlaceholderSigner(string name) : IDigitalSigner
    {
        public string CertificateName => name;
        public Task<int> GetSignatureSizeAsync() => Task.FromResult(ReservedBytes);
        public Task<byte[]> GetSignatureAsync(Stream stream) => Task.FromResult(new byte[ReservedBytes]);
    }
}
