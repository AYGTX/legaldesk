using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Queries;

public record GetDocumentByIdQuery(Guid DocumentId) : IQuery<DocumentDetailDto>;

public record DocumentDetailDto(
    Guid Id,
    string Title,
    string? Description,
    DocumentStatus Status,
    WorkflowType WorkflowType,
    string FileName,
    string StoragePath,
    string? FinalStoragePath,
    long FileSizeBytes,
    int PageCount,
    string? Subject,
    string? Message,
    string? Hash,
    DateTime CreatedAt,
    DateTime? SentAt,
    DateTime? CompletedAt,
    DateTime? ExpiresAt,
    string? VerificationCode,
    int? SlaHours,
    string? RequestTypeName,
    string? RequestTypeIcon,
    string? RequestTypeColor,
    string? AiSummary,
    DateTime? AiSummaryGeneratedAt,
    string? AiRiskAnalysisJson,
    DateTime? AiRiskAnalysisGeneratedAt,
    string? AiComplianceAnalysisJson,
    DateTime? AiComplianceAnalysisGeneratedAt,
    // Pre-flight verdict, threaded through so the detail page's intelligence band renders in the
    // same round-trip. The to-do items themselves come from GET /readiness.
    ReadinessVerdict ReadinessVerdict,
    int? ReadinessScore,
    DateTime? ReadinessEvaluatedAt,
    DateTime? ObligationDueDate,
    string? ObligationType,
    string? ObligationRationale,
    bool ObligationConfirmed,
    // Contract term. The lifecycle stage and notice deadline are derived client-side from these
    // by the same rules the alert sweep uses, so there is nothing here that can go stale.
    DateTime? ContractStartDate,
    DateTime? ContractEndDate,
    bool AutoRenews,
    int? NoticePeriodDays,
    bool TermConfirmed,
    bool HideRecipientIdentities,
    OwnerDto Owner,
    List<RecipientDto> Recipients,
    List<SignatureFieldDto> Fields,
    WorkflowDto? Workflow,
    int Version = 1,
    List<DocumentVersionDto>? Versions = null,
    string? TemplateKind = null,
    List<TemplateRevisionDto>? TemplateRevisions = null);

public record OwnerDto(Guid Id, string FullName, string Email, string? AvatarUrl);
public record RecipientDto(Guid Id, string Name, string Email, int Order, ApprovalType ApprovalType, WorkflowStepStatus Status, string SecureToken, DateTime? SignedAt, DateTime? ViewedAt, string? RoleLabel = null, string? DeclineReason = null, List<ChecklistItemDto>? ChecklistItems = null);
public record ChecklistItemDto(Guid Id, string Text, int Order, bool IsChecked);
/// <summary>
/// What changed between two versions of a template contract: the answers that differ, with who changed them and why.
/// Lets a reviewer asked to look again see exactly what the owner changed since the version they sent back.
/// </summary>
public record TemplateRevisionDto(int FromVersion, int ToVersion, string? ChangeNote, string? ChangedByName, DateTime CreatedAt, List<FieldChange> Changes);
public record DocumentVersionDto(Guid Id, int VersionNumber, long FileSizeBytes, string? ChangeNote, string? ChangedByName, DateTime CreatedAt);
public record SignatureFieldDto(Guid Id, FieldType FieldType, int Page, float X, float Y, float Width, float Height, float? Rotation, Guid? RecipientId, string? Label, string? Placeholder, bool IsRequired, bool IsSigned, DateTime? SignedAt, int ZIndex, string? FontFamily, float? FontSize, string? FontColor, string? BackgroundColor, string? BorderColor, string? Options, string? DefaultValue, string? ValidationRule, bool IsReadOnly, string? Tooltip, string? GroupName = null);
public record WorkflowDto(Guid Id, WorkflowType Type, WorkflowStepStatus Status, int CurrentStepOrder, List<WorkflowStepDto> Steps);
public record WorkflowStepDto(Guid Id, int Order, string Name, ApprovalType ApprovalType, WorkflowStepStatus Status, Guid RecipientId, bool IsParallel, DateTime? DueAt, DateTime? CompletedAt);

public class GetDocumentByIdQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetDocumentByIdQuery, DocumentDetailDto>
{
    public async Task<DocumentDetailDto> HandleAsync(GetDocumentByIdQuery query, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.AsNoTracking()
            .Include(d => d.Owner)
            .Include(d => d.RequestType)
            .Include(d => d.Recipients)
                .ThenInclude(r => r.ChecklistItems)
            .Include(d => d.SignatureFields)
            .Include(d => d.Versions)
            .Include(d => d.Workflow)
                .ThenInclude(w => w.Steps)
            .FirstOrDefaultAsync(d => d.Id == query.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), query.DocumentId);

        var userId = currentUser.UserId;
        var isAdmin = currentUser.IsInRole("Administrator");
        if (!isAdmin && document.OwnerId != userId)
        {
            var isRecipient = document.Recipients.Any(r => r.Email == currentUser.UserEmail);
            if (!isRecipient)
                throw new ForbiddenAccessException();
        }

        // A signing token is a credential: whoever holds it can act as that recipient. The owner and admins
        // manage the circuit and need them all; a participant only ever gets their own.
        var seesAllTokens = isAdmin || document.OwnerId == userId;
        var viewerEmail = currentUser.UserEmail;
        string TokenFor(Recipient r) =>
            seesAllTokens || string.Equals(r.Email, viewerEmail, StringComparison.OrdinalIgnoreCase)
                ? r.SecureToken
                : string.Empty;

        var changerIds = document.Versions.Select(v => v.ChangedById).Distinct().ToList();
        Dictionary<Guid, string> changerNames = changerIds.Count == 0
            ? []
            : await db.Users.AsNoTracking()
                .Where(u => changerIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var templateKind = TemplateRevisionSupport.KindOf(document);
        var templateRevisions = templateKind is null
            ? null
            : await BuildTemplateRevisionsAsync(document, changerNames, cancellationToken);

        return new DocumentDetailDto(
            document.Id,
            document.Title,
            document.Description,
            document.Status,
            document.WorkflowType,
            document.FileName,
            document.StoragePath,
            document.FinalStoragePath,
            document.FileSizeBytes,
            document.PageCount,
            document.Subject,
            document.Message,
            document.Hash,
            document.CreatedAt,
            document.SentAt,
            document.CompletedAt,
            document.ExpiresAt,
            document.VerificationCode,
            document.SlaHours,
            document.RequestType != null ? document.RequestType.Name : null,
            document.RequestType != null ? document.RequestType.Icon : null,
            document.RequestType != null ? document.RequestType.Color : null,
            document.AiSummary,
            document.AiSummaryGeneratedAt,
            document.AiRiskAnalysisJson,
            document.AiRiskAnalysisGeneratedAt,
            document.AiComplianceAnalysisJson,
            document.AiComplianceAnalysisGeneratedAt,
            document.ReadinessVerdict,
            document.ReadinessScore,
            document.ReadinessEvaluatedAt,
            document.ObligationDueDate,
            document.ObligationType,
            document.ObligationRationale,
            document.ObligationConfirmed,
            document.ContractStartDate,
            document.ContractEndDate,
            document.AutoRenews,
            document.NoticePeriodDays,
            document.TermConfirmed,
            document.HideRecipientIdentities,
            new OwnerDto(document.Owner.Id, document.Owner.FullName, document.Owner.Email!, document.Owner.AvatarUrl),
            document.Recipients
                .OrderBy(r => r.Order)
                .Select(r => new RecipientDto(r.Id, r.Name, r.Email, r.Order, r.ApprovalType, r.Status, TokenFor(r), r.SignedAt, r.ViewedAt, r.RoleLabel, r.DeclineReason,
                    r.ChecklistItems.OrderBy(c => c.Order).Select(c => new ChecklistItemDto(c.Id, c.Text, c.Order, c.IsChecked)).ToList()))
                .ToList(),
            document.SignatureFields
                .Select(f => new SignatureFieldDto(f.Id, f.FieldType, f.Page, f.X, f.Y, f.Width, f.Height, f.Rotation, f.RecipientId, f.Label, f.Placeholder, f.IsRequired, f.IsSigned, f.SignedAt, f.ZIndex, f.FontFamily, f.FontSize, f.FontColor, f.BackgroundColor, f.BorderColor, f.Options, f.DefaultValue, f.ValidationRule, f.IsReadOnly, f.Tooltip, f.GroupName))
                .ToList(),
            document.Workflow is null ? null : new WorkflowDto(
                document.Workflow.Id,
                document.Workflow.Type,
                document.Workflow.Status,
                document.Workflow.CurrentStepOrder,
                document.Workflow.Steps
                    .OrderBy(s => s.Order)
                    .Select(s => new WorkflowStepDto(s.Id, s.Order, s.Name, s.ApprovalType, s.Status, s.RecipientId, s.IsParallel, s.DueAt, s.CompletedAt))
                    .ToList()),
            document.Version,
            document.Versions
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => new DocumentVersionDto(
                    v.Id, v.VersionNumber, v.FileSizeBytes, v.ChangeNote,
                    changerNames.TryGetValue(v.ChangedById, out var name) ? name : null,
                    v.CreatedAt))
                .ToList(),
            templateKind,
            templateRevisions);
    }

    /// <summary>
    /// One entry per revision, oldest first: the answers a version had, against the next version's. A version kept
    /// before answers were stored with it (a file revision) cannot be compared and is skipped.
    /// </summary>
    private async Task<List<TemplateRevisionDto>> BuildTemplateRevisionsAsync(
        Document document, Dictionary<Guid, string> changerNames, CancellationToken cancellationToken)
    {
        var fields = await TemplateRevisionSupport.LoadFieldsAsync(db, document, cancellationToken) ?? [];
        var kept = document.Versions.OrderBy(v => v.VersionNumber).ToList();

        var revisions = new List<TemplateRevisionDto>();
        for (var i = 0; i < kept.Count; i++)
        {
            var from = kept[i];
            if (string.IsNullOrWhiteSpace(from.FilledValuesJson)) continue;

            var nextJson = i + 1 < kept.Count ? kept[i + 1].FilledValuesJson : document.FilledValuesJson;
            if (string.IsNullOrWhiteSpace(nextJson)) continue;

            var changes = TemplateRevisionSupport.Diff(
                TemplateRevisionSupport.ParseValues(from.FilledValuesJson),
                TemplateRevisionSupport.ParseValues(nextJson),
                fields);

            revisions.Add(new TemplateRevisionDto(
                from.VersionNumber, from.VersionNumber + 1, from.ChangeNote,
                changerNames.TryGetValue(from.ChangedById, out var name) ? name : null,
                from.CreatedAt, changes));
        }

        return revisions;
    }
}
