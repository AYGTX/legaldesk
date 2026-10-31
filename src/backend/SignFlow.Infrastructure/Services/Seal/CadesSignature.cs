using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace SignFlow.Infrastructure.Services.Seal;

/// <summary>
/// A CAdES detached signature as PAdES baseline wants it (ETSI EN 319 142-1): SHA-256, the signing-certificate-v2
/// attribute binding the certificate, no signing-time attribute (the time comes from the timestamp), and, for level T,
/// an RFC 3161 signature timestamp as an unsigned attribute.
/// </summary>
public static class CadesSignature
{
    public const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";
    public const string SignatureTimestampOid = "1.2.840.113549.1.9.16.2.14";
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    public static async Task<byte[]> SignAsync(
        byte[] content,
        X509Certificate2 certificate,
        Func<byte[], CancellationToken, Task<byte[]>>? timestamp,
        CancellationToken ct = default)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            DigestAlgorithm = new Oid(Sha256Oid),
            IncludeOption = X509IncludeOption.WholeChain
        };
        signer.SignedAttributes.Add(new AsnEncodedData(SigningCertificateV2Oid, SigningCertificateV2(certificate)));
        cms.ComputeSignature(signer, silent: true);

        if (timestamp is not null)
        {
            var signerInfo = cms.SignerInfos[0];
            var token = await timestamp(SHA256.HashData(signerInfo.GetSignature()), ct);
            signerInfo.AddUnsignedAttribute(new AsnEncodedData(SignatureTimestampOid, token));
        }

        return cms.Encode();
    }

    /// <summary>
    /// SigningCertificateV2 ::= SEQUENCE { certs SEQUENCE OF ESSCertIDv2 }, with one ESSCertIDv2 for
    /// <paramref name="certificate"/>: its SHA-256 (the default algorithm, so not written) and its issuer and serial.
    /// </summary>
    public static byte[] SigningCertificateV2(X509Certificate2 certificate)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence())
        using (writer.PushSequence())
        {
            writer.WriteOctetString(SHA256.HashData(certificate.RawData));
            using (writer.PushSequence())          // IssuerSerial
            {
                using (writer.PushSequence())      // GeneralNames
                {
                    var directoryName = new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true);
                    using (writer.PushSequence(directoryName))
                        writer.WriteEncodedValue(certificate.IssuerName.RawData);
                }
                writer.WriteInteger(certificate.GetSerialNumberBigEndian());
            }
        }
        return writer.Encode();
    }
}

/// <summary>The certificate's serial as big-endian bytes, the order ASN.1 INTEGER wants.</summary>
internal static class CertificateSerial
{
    public static ReadOnlySpan<byte> GetSerialNumberBigEndian(this X509Certificate2 certificate)
    {
        var serial = certificate.GetSerialNumber();   // little-endian
        Array.Reverse(serial);
        return serial;
    }
}
