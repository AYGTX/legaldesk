namespace SignFlow.Application.Common.Interfaces;

/// <summary>
/// The organisation's electronic seal on a completed contract (R-M5-8): a PAdES-B-T signature (CAdES detached, with an
/// RFC 3161 timestamp) over the whole final PDF.
/// </summary>
public interface IPdfSealer
{
    /// <returns>The sealed PDF.</returns>
    Task<byte[]> SealAsync(byte[] pdf, Guid organisationId, string organisationName, string reason, CancellationToken ct = default);
}

/// <summary>An RFC 3161 time-stamping authority.</summary>
public interface ITimestampAuthority
{
    /// <summary>A timestamp token (DER-encoded CMS) for a SHA-256 <paramref name="sha256"/>.</summary>
    Task<byte[]> TimestampAsync(byte[] sha256, CancellationToken ct = default);
}

/// <summary>One line of the evidence page.</summary>
public sealed record EvidenceParticipant(
    string Name,
    string Email,
    string Role,
    string Outcome,
    DateTime? At,
    string? Ip,
    string? VerificationMethod,
    DateTime? VerifiedAt,
    IReadOnlyList<string> Signatures);

/// <summary>Everything the evidence page shows (R-M5-8). Times are UTC.</summary>
public sealed record EvidenceSheet(
    string Title,
    string OrganisationName,
    string VerificationCode,
    string VerifyUrl,
    DateTime CompletedAt,
    IReadOnlyList<(int Version, string Sha256)> Versions,
    IReadOnlyList<EvidenceParticipant> Participants,
    string AuditDigest,
    int AuditEntries);

public interface IEvidencePageWriter
{
    /// <returns>The PDF with the evidence page(s) appended.</returns>
    Task<byte[]> AppendAsync(byte[] pdf, EvidenceSheet sheet, CancellationToken ct = default);
}
