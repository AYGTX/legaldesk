using System.ComponentModel.DataAnnotations;
using SignFlow.Domain.Enums;
using SignFlow.Domain.Rules;

namespace SignFlow.Domain.Entities;

public class Document : TenantEntity
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

    // Pre-flight review (M8): Harvey's findings on the current version, graded. NotEvaluated until a review of
    // this version has completed (R-M8-5); the scores are computed from the findings, never asked of a model.
    public ReadinessVerdict ReadinessVerdict { get; private set; } = ReadinessVerdict.NotEvaluated;
    public int? ReadinessScore { get; private set; }
    public int? RiskScore { get; private set; }
    public DateTime? ReadinessEvaluatedAt { get; private set; }

    /// <summary>The last completed review of the current version; its findings are the contract's current findings.</summary>
    public Guid? LastReviewRunId { get; private set; }

    // Intake (R-M2-3, R-M2-4): the text is read in the background after an upload or a new file. A PDF without a text
    // layer goes through text recognition; that text is kept here, so every review reads the same words.
    public ContractTextSource TextSource { get; private set; } = ContractTextSource.Embedded;
    public string? OcrText { get; private set; }

    /// <summary>The background reading of a new file has not finished: a review would read nothing yet.</summary>
    public bool IntakePending { get; private set; }

    // Obligation tracking (renewal/expiry date), AI-suggested at upload time and owner-confirmable
    public DateTime? ObligationDueDate { get; private set; }
    public string? ObligationType { get; private set; }
    public string? ObligationRationale { get; private set; }
    public bool ObligationConfirmed { get; private set; }
    public DateTime? LastObligationReminderSentAt { get; private set; }

    // Contract term — the lifecycle envelope the contract runs inside, distinct from an obligation:
    // an obligation is a dated duty extracted from the text, the term is "in force from X to Y,
    // auto-renews or not, notice must be given N days ahead". Entered by the owner, never guessed.
    // The notice deadline is deliberately NOT stored: it is derived from ContractEndDate and
    // NoticePeriodDays wherever it is needed, so the two can never drift apart.
    public DateTime? ContractStartDate { get; private set; }
    public DateTime? ContractEndDate { get; private set; }
    public bool AutoRenews { get; private set; }
    public int? NoticePeriodDays { get; private set; }
    public bool TermConfirmed { get; private set; }
    // The last rung of the alert ladder already sent, so each milestone fires exactly once.
    // This replaces the blunt time-based throttle used for obligation reminders.
    [MaxLength(32)]
    public string? LastTermAlertStage { get; private set; }
    public DateTime? LastTermAlertSentAt { get; private set; }

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
    public ICollection<DocumentReadinessItem> ReadinessItems { get; private set; } = [];

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

    /// <summary>Moves the contract to <paramref name="status"/>; throws if <see cref="ContractStateMachine"/> forbids it.</summary>
    public void SetStatus(DocumentStatus status)
    {
        ContractStateMachine.EnsureCanMove(Status, status);
        ApplyStatus(status);
    }

    /// <summary>
    /// Stores a status without checking the move. Only for demo data and tests that need a contract already in a
    /// given state; product code goes through <see cref="SetStatus"/>.
    /// </summary>
    public void SeedStatus(DocumentStatus status) => ApplyStatus(status);

    private void ApplyStatus(DocumentStatus status)
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
        ContractStateMachine.EnsureCanMove(Status, DocumentStatus.InProgress);
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
        ContractStateMachine.EnsureCanMove(Status, DocumentStatus.Draft);
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
        // The text of the previous file is gone with it; the new file is read again by the intake job.
        TextSource = ContractTextSource.Embedded;
        OcrText = null;
        // The review described the previous file: sending is refused until this version is reviewed (R-M7-6).
        // The findings are kept: the next review marks the ones it no longer finds as Fixed in this version.
        ClearReview();
        // The contract term is deliberately kept, like a human-confirmed obligation date: the paper
        // changed, but the dates the parties negotiated did not.
        if (SlaHours.HasValue)
            ExpiresAt = DateTime.UtcNow.AddHours(SlaHours.Value);
        else if (ExpirationDays.HasValue)
            ExpiresAt = DateTime.UtcNow.AddDays(ExpirationDays.Value);
        Status = DocumentStatus.Draft;
        CompletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// A template contract revised by changing its answers: the file and the field layout stay as they are, only the
    /// filled values change. Everything else is the same as a revision upload — new version, back to Draft, analyses
    /// and readiness cleared. The calling command keeps the outgoing version's answers on a DocumentVersion row.
    /// </summary>
    public void ApplyTemplateRevision(string filledValuesJson)
    {
        ApplyRevision(FileName, StoragePath, FileSizeBytes, PageCount, Hash ?? string.Empty);
        FilledValuesJson = filledValuesJson;
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

    /// <summary>
    /// Records the grade of the current findings: after a completed review, and after an owner's decision on a finding,
    /// which changes the verdict without re-reviewing.
    /// </summary>
    public void SetReview(Guid runId, ReadinessVerdict verdict, int readinessScore, int riskScore)
    {
        LastReviewRunId = runId;
        ReadinessVerdict = verdict;
        ReadinessScore = readinessScore;
        RiskScore = riskScore;
        ReadinessEvaluatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>A new file was stored: its text is read in the background before any review.</summary>
    public void MarkIntakePending()
    {
        IntakePending = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>The intake job read the file: from its text layer, by text recognition, or not at all.</summary>
    public void CompleteIntake(ContractTextSource source, string? ocrText)
    {
        TextSource = source;
        OcrText = source == ContractTextSource.Ocr ? ocrText : null;
        IntakePending = false;
        UpdatedAt = DateTime.UtcNow;
    }

    private void ClearReview()
    {
        LastReviewRunId = null;
        ReadinessVerdict = ReadinessVerdict.NotEvaluated;
        ReadinessScore = null;
        RiskScore = null;
        ReadinessEvaluatedAt = null;
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

    /// <summary>
    /// Records the negotiated contract term. Re-dating a contract clears the alert ladder so the
    /// milestones are re-armed against the new end date — otherwise moving an end date forward
    /// would silently skip every rung already sent against the old one.
    /// </summary>
    public void SetContractTerm(DateTime? startDate, DateTime? endDate, bool autoRenews, int? noticePeriodDays)
    {
        ContractStartDate = startDate;
        ContractEndDate = endDate;
        AutoRenews = autoRenews;
        NoticePeriodDays = noticePeriodDays;
        TermConfirmed = true;
        LastTermAlertStage = null;
        LastTermAlertSentAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>A term read from the contract by the AI assistant: recorded so the contract appears in the schedule,
    /// but left unconfirmed until a person saves the term.</summary>
    public void SuggestContractTerm(DateTime? startDate, DateTime? endDate, bool autoRenews, int? noticePeriodDays)
    {
        SetContractTerm(startDate, endDate, autoRenews, noticePeriodDays);
        TermConfirmed = false;
    }

    /// <summary>Records which rung of the alert ladder has been sent, so it never fires twice.</summary>
    public void MarkTermAlertSent(string stage)
    {
        LastTermAlertStage = stage;
        LastTermAlertSentAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsExpired() => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
}
