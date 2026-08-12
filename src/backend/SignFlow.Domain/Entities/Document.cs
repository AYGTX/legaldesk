using SignFlow.Domain.Enums;

namespace SignFlow.Domain.Entities;

public class Document : BaseEntity
{
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;
    public string FileName { get; private set; } = string.Empty;
    public string StoragePath { get; private set; } = string.Empty;
    public string? FinalStoragePath { get; private set; }
    public long FileSizeBytes { get; private set; }
    public int PageCount { get; private set; }
    public string ContentType { get; private set; } = "application/pdf";
    public Guid OwnerId { get; private set; }
    public ApplicationUser Owner { get; private set; } = null!;
    public string? FolderId { get; private set; }
    public bool IsTemplate { get; private set; }
    public int? ExpirationDays { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public string? Message { get; private set; }
    public string? Subject { get; private set; }
    public string? Hash { get; private set; }
    public string? Tags { get; private set; }
    public bool IsPublic { get; private set; }
    public string? VerificationCode { get; private set; }
    public int Version { get; private set; } = 1;
    public Guid? ParentDocumentId { get; private set; }
    public bool HasWatermark { get; private set; }
    public string? WatermarkText { get; private set; }
    public bool HideRecipientIdentities { get; private set; }

    // AI-generated summary (persisted so all parties can read it without re-calling the AI)
    public string? AiSummary { get; private set; }
    public DateTime? AiSummaryGeneratedAt { get; private set; }

    // AI risk-clause analysis (JSON-serialized ContractRiskAnalysis), generated at upload time
    public string? AiRiskAnalysisJson { get; private set; }
    public DateTime? AiRiskAnalysisGeneratedAt { get; private set; }

    // AI compliance analysis (JSON-serialized ComplianceAnalysis), generated on demand
    public string? AiComplianceAnalysisJson { get; private set; }
    public DateTime? AiComplianceAnalysisGeneratedAt { get; private set; }

    // Obligation tracking (renewal/expiry date), AI-suggested at upload time and owner-confirmable
    public DateTime? ObligationDueDate { get; private set; }
    public string? ObligationType { get; private set; }
    public string? ObligationRationale { get; private set; }
    public bool ObligationConfirmed { get; private set; }
    public DateTime? LastObligationReminderSentAt { get; private set; }

    // Request type & SLA (snapshot taken at creation so later config changes don't affect in-flight requests)
    public Guid? RequestTypeId { get; private set; }
    public RequestType? RequestType { get; private set; }
    public int? SlaHours { get; private set; }

    public WorkflowType WorkflowType { get; private set; } = WorkflowType.Sequential;

    // HTML-template source (null for PDF-based documents)
    public Guid? HtmlTemplateId { get; private set; }
    public HtmlTemplate? HtmlTemplate { get; private set; }

    // PDF-template source (null for one-off PDF-designer and HTML-template documents).
    // Set when the document was instantiated from a reusable PdfTemplate.
    public Guid? PdfTemplateId { get; private set; }
    public PdfTemplate? PdfTemplate { get; private set; }

    /// <summary>JSON dictionary of fieldKey → value, filled by the initiator at request-creation time.</summary>
    public string? FilledValuesJson { get; private set; }

    /// <summary>
    /// Snapshot of the source HtmlTemplate's raw HtmlContent at request-creation time. Rendering
    /// (signing page, final PDF) always uses this instead of re-reading the live template, so the
    /// document is unaffected by the template being edited or deleted afterward.
    /// </summary>
    public string? HtmlContentSnapshot { get; private set; }

    /// <summary>SQL Server rowversion concurrency token — concurrent updates fail fast instead of last-write-wins.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public Workflow? Workflow { get; private set; }
    public ICollection<DocumentVersion> Versions { get; private set; } = [];
    public ICollection<Recipient> Recipients { get; private set; } = [];
    public ICollection<SignatureField> SignatureFields { get; private set; } = [];
    public ICollection<AuditLog> AuditLogs { get; private set; } = [];
    public ICollection<Comment> Comments { get; private set; } = [];

    private Document() { }

    public static Document Create(
        string title,
        string fileName,
        string storagePath,
        long fileSizeBytes,
        int pageCount,
        Guid ownerId,
        string? description = null,
        int? expirationDays = null,
        string? subject = null,
        string? message = null)
    {
        var document = new Document
        {
            Title = title,
            FileName = fileName,
            StoragePath = storagePath,
            FileSizeBytes = fileSizeBytes,
            PageCount = pageCount,
            OwnerId = ownerId,
            Description = description,
            ExpirationDays = expirationDays,
            Subject = subject,
            Message = message,
            VerificationCode = Guid.NewGuid().ToString("N")[..16].ToUpper()
        };

        if (expirationDays.HasValue)
            document.ExpiresAt = DateTime.UtcNow.AddDays(expirationDays.Value);

        return document;
    }

    public void UpdateDetails(string title, string? description, string? subject, string? message, int? expirationDays)
    {
        Title = title;
        Description = description;
        Subject = subject;
        Message = message;
        ExpirationDays = expirationDays;
        if (expirationDays.HasValue)
            ExpiresAt = DateTime.UtcNow.AddDays(expirationDays.Value);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetStatus(DocumentStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;

        if (status == DocumentStatus.Completed)
            CompletedAt = DateTime.UtcNow;
        if (status == DocumentStatus.InProgress)
            SentAt = DateTime.UtcNow;
    }

    public void SetFinalStoragePath(string path) => FinalStoragePath = path;
    public void SetHash(string hash) => Hash = hash;
    public void SetPageCount(int pageCount) => PageCount = pageCount;

    /// <summary>Applies a request type and snapshots its SLA, computing the expiry from the creation timestamp.</summary>
    public void SetWorkflowType(WorkflowType type) => WorkflowType = type;

    public void ApplyRequestType(Guid requestTypeId, int slaHours)
    {
        RequestTypeId = requestTypeId;
        SlaHours = slaHours;
        ExpiresAt = CreatedAt.AddHours(slaHours);
    }

    /// <summary>Admin reopen: restarts the SLA clock from now and reactivates the workflow.</summary>
    public void Reopen(int? slaHours)
    {
        var hours = slaHours ?? SlaHours;
        if (hours.HasValue)
            ExpiresAt = DateTime.UtcNow.AddHours(hours.Value);
        else
            ExpiresAt = null;
        SlaHours = hours;
        Status = DocumentStatus.InProgress;
        CompletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Replaces the document file with a revised upload after changes were requested.
    /// The document returns to Draft so the owner can re-place fields (page coordinates may
    /// shift in the new PDF) and explicitly re-send; the SLA clock restarts like on Reopen.
    /// The previous file is preserved as a DocumentVersion row by the calling command.
    /// </summary>
    public void ApplyRevision(string fileName, string storagePath, long fileSizeBytes, int pageCount, string hash)
    {
        FileName = fileName;
        StoragePath = storagePath;
        FileSizeBytes = fileSizeBytes;
        PageCount = pageCount;
        Hash = hash;
        Version++;
        FinalStoragePath = null;
        // The cached summary/risk analysis describe the previous file's content
        AiSummary = null;
        AiSummaryGeneratedAt = null;
        AiRiskAnalysisJson = null;
        AiRiskAnalysisGeneratedAt = null;
        AiComplianceAnalysisJson = null;
        AiComplianceAnalysisGeneratedAt = null;
        if (SlaHours.HasValue)
            ExpiresAt = DateTime.UtcNow.AddHours(SlaHours.Value);
        else if (ExpirationDays.HasValue)
            ExpiresAt = DateTime.UtcNow.AddDays(ExpirationDays.Value);
        Status = DocumentStatus.Draft;
        CompletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetHtmlSource(Guid htmlTemplateId, string filledValuesJson, string htmlContentSnapshot)
    {
        HtmlTemplateId = htmlTemplateId;
        FilledValuesJson = filledValuesJson;
        HtmlContentSnapshot = htmlContentSnapshot;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Links this document to the reusable PDF template it was instantiated from and snapshots the
    /// initiator-filled values. The base PDF itself is a per-instance copy of the template's stored file.
    /// </summary>
    public void SetPdfTemplateSource(Guid pdfTemplateId, string filledValuesJson)
    {
        PdfTemplateId = pdfTemplateId;
        FilledValuesJson = filledValuesJson;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetHideRecipientIdentities(bool hide)
    {
        HideRecipientIdentities = hide;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetAiSummary(string summary)
    {
        AiSummary = summary;
        AiSummaryGeneratedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetAiRiskAnalysis(string riskAnalysisJson)
    {
        AiRiskAnalysisJson = riskAnalysisJson;
        AiRiskAnalysisGeneratedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetAiComplianceAnalysis(string complianceAnalysisJson)
    {
        AiComplianceAnalysisJson = complianceAnalysisJson;
        AiComplianceAnalysisGeneratedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>AI-suggested obligation date at upload/analysis time — not yet confirmed by the owner.</summary>
    public void SetObligationSuggestion(DateTime? dueDate, string? obligationType, string? rationale)
    {
        ObligationDueDate = dueDate;
        ObligationType = obligationType;
        ObligationRationale = rationale;
        ObligationConfirmed = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Owner override/confirmation of the obligation date — takes precedence over the AI suggestion.</summary>
    public void ConfirmObligation(DateTime dueDate, string obligationType)
    {
        ObligationDueDate = dueDate;
        ObligationType = obligationType;
        ObligationRationale = null;
        ObligationConfirmed = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkObligationReminderSent()
    {
        LastObligationReminderSentAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsExpired() => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
}
