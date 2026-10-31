using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Common.Services;

/// <summary>What a version was (R-M7-4): the SHA-256 of its file and of its words, where the words came from, and the
/// round that asked people to act on it. Stamped on the outgoing version, before the revision replaces it.</summary>
public static class VersionFingerprints
{
    public static async Task StampAsync(IApplicationDbContext db, DocumentVersion version, Document document,
        IContractTextProvider? texts, CancellationToken ct = default)
    {
        string? textSha = null;
        if (texts is not null)
        {
            try
            {
                textSha = TextSha256(await texts.GetTextAsync(document, ct));
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // A version whose words cannot be read still keeps its file fingerprint; the revision is not refused.
            }
        }

        var roundId = await db.ContractRounds
            .Where(r => r.DocumentId == document.Id && r.VersionNumber == version.VersionNumber)
            .OrderByDescending(r => r.Number)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

        version.Fingerprint(document.Hash, textSha, document.HtmlTemplateId is null ? document.TextSource : null, roundId);
    }

    /// <summary>Line endings and trailing spaces do not change what a contract says.</summary>
    public static string TextSha256(string text)
    {
        var normalised = string.Join('\n', text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())).Trim();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }
}
