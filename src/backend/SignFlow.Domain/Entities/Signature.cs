using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class Signature : TenantEntity
{
    public Guid SignatureFieldId { get; private set; }
    public SignatureField SignatureField { get; private set; } = null!;
    public Guid RecipientId { get; private set; }
    public Recipient Recipient { get; private set; } = null!;
    public SignatureType SignatureType { get; private set; }
    public string? ImageUrl { get; private set; }
    public string? TypedText { get; private set; }
    public string? FontFamily { get; private set; }
    public string Ip { get; private set; } = string.Empty;
    public string UserAgent { get; private set; } = string.Empty;
    public string? Location { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public string? Certificate { get; private set; }
    public string? Hash { get; private set; }
    public DateTime SignedAt { get; private set; } = DateTime.UtcNow;

    private Signature() { }

    public static Signature Create(
        Guid signatureFieldId,
        Guid recipientId,
        SignatureType type,
        string ip,
        string userAgent,
        string? imageUrl = null,
        string? typedText = null,
        string? fontFamily = null,
        string? location = null)
        => new()
        {
            SignatureFieldId = signatureFieldId,
            RecipientId = recipientId,
            SignatureType = type,
            Ip = ip,
            UserAgent = userAgent,
            ImageUrl = imageUrl,
            TypedText = typedText,
            FontFamily = fontFamily,
            Location = location
        };

    public void SetCertificate(string certificate) => Certificate = certificate;
    public void SetHash(string hash) => Hash = hash;
    public void SetCoordinates(double lat, double lng) { Latitude = lat; Longitude = lng; }
}
