namespace SignFlow.Domain.Entities;

public class Template : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public int PageCount { get; private set; }
    public Guid OwnerId { get; private set; }
    public ApplicationUser Owner { get; private set; } = null!;
    public string? Tags { get; private set; }
    public bool IsPublic { get; private set; }
    public string? WorkflowJson { get; private set; }
    public string? FieldsJson { get; private set; }
    public string? Variables { get; private set; }
    public string? Category { get; private set; }
    public int UsageCount { get; private set; }
    public string? ThumbnailUrl { get; private set; }

    private Template() { }

    public static Template Create(string name, string storagePath, string fileName, long fileSizeBytes, int pageCount, Guid ownerId, string? description = null)
        => new()
        {
            Name = name,
            StoragePath = storagePath,
            FileName = fileName,
            FileSizeBytes = fileSizeBytes,
            PageCount = pageCount,
            OwnerId = ownerId,
            Description = description
        };

    public void IncrementUsage() => UsageCount++;
    public void SetWorkflow(string workflowJson) => WorkflowJson = workflowJson;
    public void SetFields(string fieldsJson) => FieldsJson = fieldsJson;
    public void SetPublic(bool isPublic) => IsPublic = isPublic;
}
