using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class DocumentVersion : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public int VersionNumber { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public string? ChangeNote { get; private set; }
    public Guid ChangedById { get; private set; }

    /// <summary>
    /// For contracts built from a template: the answers (fieldKey to value, JSON) this version carried. The file
    /// of a template contract never changes between versions, so the answers are what a version actually was.
    /// </summary>
    public string? FilledValuesJson { get; private set; }

    public void SetFilledValues(string? filledValuesJson) => FilledValuesJson = filledValuesJson;

    // What the version was (R-M7-4): its file and text fingerprints, where its text came from, and its round.
    public string? FileSha256 { get; private set; }
    public string? TextSha256 { get; private set; }
    public ContractTextSource? TextSource { get; private set; }
    public Guid? RoundId { get; private set; }

    public void Fingerprint(string? fileSha256, string? textSha256, ContractTextSource? textSource, Guid? roundId)
    {
        FileSha256 = fileSha256?.ToLowerInvariant();
        TextSha256 = textSha256;
        TextSource = textSource;
        RoundId = roundId;
    }

    private DocumentVersion() { }

    public static DocumentVersion Create(Guid documentId, int versionNumber, string storagePath, long fileSizeBytes, Guid changedById, string? changeNote = null)
        => new()
        {
            DocumentId = documentId,
            VersionNumber = versionNumber,
            StoragePath = storagePath,
            FileSizeBytes = fileSizeBytes,
            ChangedById = changedById,
            ChangeNote = changeNote
        };
}
