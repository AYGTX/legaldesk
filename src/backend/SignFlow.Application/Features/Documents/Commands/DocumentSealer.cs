using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>Background work on a completed contract.</summary>
public interface IDocumentJobs
{
    /// <summary>Adds the evidence page and seals the final PDF (R-M5-8); retried until the seal and timestamp succeed.</summary>
    void EnqueueSeal(Guid documentId, Guid organisationId);
}

public interface IDocumentSealer
{
    Task SealAsync(Guid documentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The last step of a contract (R-M5-8): the final PDF (stamped fields) gets an evidence page (participants, actions,
/// times, IPs, how each proved their address, the SHA-256 of every version, the audit digest), then the organisation's
/// PAdES-B-T seal with an RFC 3161 timestamp. The sealed file replaces the final one; its SHA-256 is stored on the
/// contract and in the audit trail, and the public verification page compares uploads with it (R-M5-9).
/// </summary>
public class DocumentSealer(
    IApplicationDbContext db,
    IStorageService storage,
    IEvidencePageWriter evidence,
    IPdfSealer sealer,
    IAuditService audit,
    IOptions<SecurityOptions> security,
    ILogger<DocumentSealer> logger) : IDocumentSealer
{
    public async Task SealAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.Recipients)
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null || document.Status != DocumentStatus.Completed || document.SealedAt is not null)
            return;

        var path = document.FinalStoragePath ?? document.StoragePath;
        if (path.StartsWith("html-templates/", StringComparison.Ordinal))
        {
            logger.LogError("Contract {DocumentId} is complete but has no final PDF to seal", document.Id);
            return;
        }

        var organisation = await db.Organisations.AsNoTracking().FirstAsync(o => o.Id == document.OrganisationId, cancellationToken);
        var pdf = await ReadAsync(path, cancellationToken);
        var sheet = await BuildSheetAsync(document, organisation, cancellationToken);

        var withEvidence = await evidence.AppendAsync(pdf, sheet, cancellationToken);
        var sealedPdf = await sealer.SealAsync(withEvidence, organisation.Id, organisation.Name,
            "Contrat signé par toutes les parties / Signed by all parties", cancellationToken);
        var sha = Convert.ToHexStringLower(SHA256.HashData(sealedPdf));

        using (var stream = new MemoryStream(sealedPdf))
        {
            var sealedPath = await storage.UploadAsync(stream, $"sealed_{document.FileName}", "application/pdf",
                $"completed/{document.Id}", cancellationToken);
            document.Seal(sealedPath, sha);
        }
        await db.SaveChangesAsync(cancellationToken);

        await audit.LogAsync(document.Id, AuditAction.DocumentSealed, "system",
            $"Final PDF sealed by {organisation.Name} with a timestamp; SHA-256 {sha}; verification code {document.VerificationCode}",
            cancellationToken: cancellationToken);
        logger.LogInformation("Contract {DocumentId} sealed ({Sha})", document.Id, sha);
    }

    private async Task<EvidenceSheet> BuildSheetAsync(Document document, Organisation organisation, CancellationToken ct)
    {
        var versions = new List<(int, string)>();
        foreach (var v in document.Versions.OrderBy(v => v.VersionNumber))
            versions.Add((v.VersionNumber, await HashOfAsync(v.StoragePath, ct)));
        versions.Add((document.Version, document.Hash?.ToLowerInvariant() ?? await HashOfAsync(document.StoragePath, ct)));

        var signatures = await db.Signatures.AsNoTracking()
            .Include(s => s.SignatureField)
            .Where(s => s.SignatureField.DocumentId == document.Id)
            .OrderBy(s => s.SignedAt)
            .ToListAsync(ct);

        var participants = document.Recipients
            .OrderBy(r => r.Order)
            .Select(r => new EvidenceParticipant(
                r.Name, r.Email, r.RoleLabel ?? r.ApprovalType.ToString(), r.Status.ToString(), r.SignedAt, r.SignedIp,
                r.VerificationMethod, r.VerifiedAt,
                [.. signatures.Where(s => s.RecipientId == r.Id).Select(s =>
                    $"{s.SignatureField.FieldType} p.{s.SignatureField.Page} · {s.SignedAt:yyyy-MM-dd HH:mm:ss} UTC · IP {s.Ip}" +
                    (s.DocumentVersion is { } dv ? $" · v{dv}" : string.Empty) +
                    (s.DocumentSha256 is { } ds ? $" · file {ds[..Math.Min(16, ds.Length)]}…" : string.Empty) +
                    (s.ImageSha256 is { } img ? $" · image {img[..16]}…" : string.Empty))]))
            .ToList();

        // The audit trail's digest: anyone holding the export can recompute it and see that nothing was changed.
        var entries = await db.AuditLogs.AsNoTracking()
            .Where(a => a.DocumentId == document.Id)
            .OrderBy(a => a.Timestamp).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.Timestamp, a.Action, a.Description })
            .ToListAsync(ct);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
            entries.Select(e => $"{e.Id}|{e.Timestamp:O}|{e.Action}|{e.Description}")))));

        return new EvidenceSheet(
            document.Title, organisation.Name, document.VerificationCode ?? string.Empty,
            $"{security.Value.PublicUrl.TrimEnd('/')}/verify/{document.VerificationCode}",
            document.CompletedAt ?? DateTime.UtcNow, versions, participants, digest, entries.Count);
    }

    private async Task<byte[]> ReadAsync(string path, CancellationToken ct)
    {
        await using var stream = await storage.DownloadAsync(path, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private async Task<string> HashOfAsync(string path, CancellationToken ct)
    {
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(await ReadAsync(path, ct)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Version file {Path} could not be read for the evidence page", path);
            return "unavailable";
        }
    }
}
