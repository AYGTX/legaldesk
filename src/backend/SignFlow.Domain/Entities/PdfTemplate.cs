using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

/// <summary>
/// A reusable request template backed by a pre-existing PDF that is uploaded once and persisted.
/// Every request created from this template reuses the same stored PDF as its base document — the
/// stored file is never regenerated, only copied per instance so the original stays pristine.
/// This is the PDF analogue of <see cref="HtmlTemplate"/>.
/// </summary>
public class PdfTemplate : VersionedTemplate
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>Storage path of the persisted base PDF (uploaded once, reused for every instance).</summary>
    public string StoragePath { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public int PageCount { get; private set; }


    /// <summary>How recipients created from this template are routed: Sequential, Parallel, or Mixed.</summary>
    public WorkflowType WorkflowType { get; private set; } = WorkflowType.Sequential;

    public bool HideRecipientIdentities { get; private set; }

    /// <summary>
    /// Request type carried by this template — requests created from it inherit this type (and its SLA)
    /// at creation time, as a snapshot. Changing this later only affects requests created afterward.
    /// </summary>
    public Guid? RequestTypeId { get; private set; }
    public RequestType? RequestType { get; private set; }


    /// <summary>
    /// The reusable approval path this template follows, when it has one. The preset then decides who approves each
    /// signature field (see <see cref="WorkflowPreset"/>); without one, the default approver and per-field choices apply.
    /// </summary>
    public Guid? WorkflowPresetId { get; private set; }
    public WorkflowPreset? WorkflowPreset { get; private set; }

    public void SetWorkflowPreset(Guid? presetId)
    {
        WorkflowPresetId = presetId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Library clauses appended, as an annex, to every request made from this template (a JSON array of clause ids).
    /// A PDF template is a fixed file, so its own text cannot take a clause: the wording goes on extra pages after it.
    /// </summary>
    public string? AnnexClauseIdsJson { get; private set; }

    [NotMapped]
    public IReadOnlyList<Guid> AnnexClauseIds =>
        string.IsNullOrWhiteSpace(AnnexClauseIdsJson) ? [] : JsonSerializer.Deserialize<List<Guid>>(AnnexClauseIdsJson) ?? [];

    public void SetAnnexClauses(IEnumerable<Guid> clauseIds)
    {
        var ids = clauseIds.Distinct().ToList();
        AnnexClauseIdsJson = ids.Count == 0 ? null : JsonSerializer.Serialize(ids);
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid CreatedById { get; private set; }
    public ApplicationUser CreatedByUser { get; private set; } = null!;

    public ICollection<PdfTemplateField> Fields { get; private set; } = [];

    private PdfTemplate() { }

    public static PdfTemplate Create(
        string name,
        string storagePath,
        string fileName,
        long fileSizeBytes,
        int pageCount,
        Guid createdById,
        Guid? requestTypeId = null,
        string? description = null,
        WorkflowType workflowType = WorkflowType.Sequential,
        bool hideRecipientIdentities = false)
        => new()
        {
            Name = name,
            StoragePath = storagePath,
            FileName = fileName,
            FileSizeBytes = fileSizeBytes,
            PageCount = pageCount,
            CreatedById = createdById,
            Description = description,
            WorkflowType = workflowType,
            HideRecipientIdentities = hideRecipientIdentities,
            RequestTypeId = requestTypeId
        };

    /// <summary>
    /// Points the template at an edited copy of its PDF. Requests already created keep their own copy; only
    /// requests created afterwards use the new file. The previous file stays in storage.
    /// </summary>
    public void ReplaceFile(string storagePath, long fileSizeBytes, int pageCount)
    {
        StoragePath = storagePath;
        FileSizeBytes = fileSizeBytes;
        PageCount = pageCount;
        Edited();
    }

    /// <summary>Updates metadata/config. The stored PDF is replaced only through <see cref="ReplaceFile"/>.</summary>
    public void Update(
        string name,
        string? description,
        bool isActive,
        WorkflowType workflowType,
        bool hideRecipientIdentities,
        Guid? requestTypeId)
    {
        Name = name;
        Description = description;
        IsActive = isActive;
        WorkflowType = workflowType;
        HideRecipientIdentities = hideRecipientIdentities;
        RequestTypeId = requestTypeId;
        Edited();
    }

    /// <summary>
    /// The next version of an approved template (R-M6-5): a draft copy pointing at the same stored PDF, which is never
    /// changed in place. The fields are copied by the caller.
    /// </summary>
    public PdfTemplate NextVersion(Guid createdById)
    {
        var next = Create(Name, StoragePath, FileName, FileSizeBytes, PageCount, createdById, RequestTypeId, Description, WorkflowType, HideRecipientIdentities);
        next.WorkflowPresetId = WorkflowPresetId;
        next.AnnexClauseIdsJson = AnnexClauseIdsJson;
        next.IsActive = IsActive;
        next.BecomeNextVersionOf(this);
        return next;
    }
}
