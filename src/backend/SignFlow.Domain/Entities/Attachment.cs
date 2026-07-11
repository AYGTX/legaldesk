namespace SignFlow.Domain.Entities;

public class Attachment : BaseEntity
{
    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public Guid? RecipientId { get; private set; }
    public Guid? FieldId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string StoragePath { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public string? Description { get; private set; }

    private Attachment() { }

    public static Attachment Create(Guid documentId, string fileName, string storagePath, string contentType, long fileSizeBytes, Guid? recipientId = null, Guid? fieldId = null)
        => new()
        {
            DocumentId = documentId,
            FileName = fileName,
            StoragePath = storagePath,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            RecipientId = recipientId,
            FieldId = fieldId
        };
}
