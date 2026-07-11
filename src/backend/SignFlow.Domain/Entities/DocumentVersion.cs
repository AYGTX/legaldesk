namespace SignFlow.Domain.Entities;

public class DocumentVersion : BaseEntity
{
    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public int VersionNumber { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public string? ChangeNote { get; private set; }
    public Guid ChangedById { get; private set; }

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
