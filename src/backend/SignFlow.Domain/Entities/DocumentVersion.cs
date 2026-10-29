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
