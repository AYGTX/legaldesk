using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class Signature : TenantEntity
{
    public Guid SignatureFieldId { get; private set; }
    public SignatureField SignatureField { get; private set; } = null!;
    public Guid RecipientId { get; private set; }
    /// <summary>The round the signature was given in (R-M7-2).</summary>
    public Guid? RoundId { get; private set; }
    public Recipient Recipient { get; private set; } = null!;
    public SignatureType SignatureType { get; private set; }
    public string? ImageUrl { get; private set; }
    public string? TypedText { get; private set; }
    public string? FontFamily { get; private set; }
    public string Ip { get; private set; } = string.Empty;
    public string UserAgent { get; private set; } = string.Empty;
    public DateTime SignedAt { get; private set; } = DateTime.UtcNow;

    // Evidence (R-M5-5): who proved what, on which exact file.
    public string? VerifiedEmail { get; private set; }
    public string? VerificationMethod { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public int? DocumentVersion { get; private set; }
    /// <summary>SHA-256 of the file the participant was shown.</summary>
    public string? DocumentSha256 { get; private set; }
    /// <summary>SHA-256 of the stored signature image, after the server re-encoded it.</summary>
    public string? ImageSha256 { get; private set; }

    private Signature() { }

    public static Signature Create(
        Guid signatureFieldId,
        Guid recipientId,
        SignatureType type,
        string ip,
        string userAgent,
        string? imageUrl = null,
        string? typedText = null,
        string? fontFamily = null)
        => new()
        {
            SignatureFieldId = signatureFieldId,
            RecipientId = recipientId,
            SignatureType = type,
            Ip = ip,
            UserAgent = userAgent,
            ImageUrl = imageUrl,
            TypedText = typedText,
            FontFamily = fontFamily
        };

    public void InRound(Guid? roundId) => RoundId = roundId;

    public void RecordEvidence(string? verifiedEmail, string? method, DateTime? verifiedAt, int documentVersion,
        string? documentSha256, string? imageSha256)
    {
        VerifiedEmail = verifiedEmail;
        VerificationMethod = method;
        VerifiedAt = verifiedAt;
        DocumentVersion = documentVersion;
        DocumentSha256 = documentSha256;
        ImageSha256 = imageSha256;
    }
}
