using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Signing.Queries;

public record GetSigningSessionQuery(string SecureToken) : IQuery<SigningSessionDto>;

public record SigningSessionDto(
    Guid DocumentId,
    string DocumentTitle,
    string StoragePath,
    int PageCount,
    string? Message,
    RecipientSessionDto Recipient,
    List<AssignedFieldDto> Fields,
    bool IsCompleted,
    bool IsRejected,
    bool IsExpired,
    bool IsMyTurn,
    string? WaitingForName,
    DateTime? ExpiresAt,
    int? SlaHours,
    List<WorkflowParticipantDto> Participants,
    // HTML template fields (null for PDF-based documents)
    bool IsHtmlTemplate = false,
    string? RenderedHtmlContent = null,
    // Expiry of this specific signing link (distinct from the document's overall SLA deadline above)
    DateTime? LinkExpiresAt = null,
    // Lets the signing UI show the paused/changes-requested state to every participant
    DocumentStatus DocumentStatus = DocumentStatus.InProgress,
    // What the participant must read before acting, and whether they already confirmed it
    ReviewGateDto? ReviewGate = null);

/// <param name="Required">The participant has a task, so they must read the document (and the review) before acting.</param>
/// <param name="Acknowledged">They already confirmed it.</param>
/// <param name="ReviewRestricted">
/// The AI review is not shown to this participant. It lists risks and gaps in the contract, which is the owner's
/// side of the table: it is shown to people with an account in the organisation, not to outside signers.
/// </param>
/// <param name="Review">The stored review, when there is one and the participant may see it.</param>
public record ReviewGateDto(bool Required, bool Acknowledged, bool ReviewRestricted, AiReviewDto? Review);

public record AiReviewDto(
    ReadinessVerdict Verdict,
    int? Score,
    int? RiskScore,
    int? ComplianceScore,
    int OpenCount,
    int CriticalCount,
    int HandledCount,
    List<AiReviewItemDto> Items);

public record AiReviewItemDto(
    string Severity,
    string Title,
    string Detail,
    string? WhyItMatters,
    string? RecommendedAction,
    ReadinessItemStatus Status,
    string? ResolutionNote);

public record RecipientSessionDto(Guid Id, string Name, string Email, ApprovalType ApprovalType, WorkflowStepStatus Status, string? RoleLabel = null, List<ChecklistItemSessionDto>? ChecklistItems = null);
public record ChecklistItemSessionDto(Guid Id, string Text, int Order, bool IsChecked);

public record WorkflowParticipantDto(int Order, string Name, WorkflowStepStatus Status, bool IsCurrent, bool IsMe, DateTime? SignedAt);
public record AssignedFieldDto(Guid Id, FieldType FieldType, int Page, float X, float Y, float Width, float Height, float? Rotation, string? Label, string? Placeholder, bool IsRequired, bool IsSigned, string? DefaultValue, string? Options, string? Tooltip, string? FontFamily, float? FontSize, string? FontColor, string? GroupName = null);

public class GetSigningSessionQueryHandler(IApplicationDbContext db) : IQueryHandler<GetSigningSessionQuery, SigningSessionDto>
{
    public async Task<SigningSessionDto> HandleAsync(GetSigningSessionQuery query, CancellationToken cancellationToken = default)
    {
        var recipient = await db.Recipients
            .AsNoTracking()
            .Include(r => r.Document)
            .Include(r => r.AssignedFields)
            .Include(r => r.ChecklistItems)
            .FirstOrDefaultAsync(r => r.SecureToken == query.SecureToken, cancellationToken)
            ?? throw new NotFoundException("Signing session not found or has expired.");

        if (recipient.TokenExpiresAt.HasValue && recipient.TokenExpiresAt.Value < DateTime.UtcNow)
            throw new ForbiddenAccessException("This signing link has expired.");

        // Full ordered participant list — needed for both the visualizer and the orphan fallback below.
        var allRecipients = await db.Recipients.AsNoTracking()
            .Where(r => r.DocumentId == recipient.DocumentId)
            .OrderBy(r => r.Order)
            .Select(r => new { r.Id, r.Name, r.Order, r.Status, r.IsRequired, r.SignedAt, r.ApprovalType })
            .ToListAsync(cancellationToken);

        // Resolve fields for this recipient.
        // Primary: fields explicitly linked by RecipientId.
        // Fallback (content signers only): if none found, load fields whose RecipientId is
        // stale/orphaned (i.e. doesn't correspond to any current recipient). This recovers
        // documents saved before the client-ID preservation fix was applied. Reviewers,
        // approvers, and viewers never inherit orphaned fields — their session has no fields.
        var assignedFields = recipient.AssignedFields.ToList();
        var isContentSigner = recipient.ApprovalType is ApprovalType.Signature or ApprovalType.Stamp;
        if (!assignedFields.Any() && isContentSigner)
        {
            var validIds = allRecipients.Select(r => r.Id).ToHashSet();
            assignedFields = await db.SignatureFields
                .AsNoTracking()
                .Where(f => f.DocumentId == recipient.DocumentId
                         && (f.RecipientId == null || !validIds.Contains(f.RecipientId.Value)))
                .ToListAsync(cancellationToken);
        }

        var fields = assignedFields
            .Select(f => new AssignedFieldDto(f.Id, f.FieldType, f.Page, f.X, f.Y, f.Width, f.Height, f.Rotation,
                f.Label, f.Placeholder, f.IsRequired, f.IsSigned, f.DefaultValue, f.Options, f.Tooltip, f.FontFamily, f.FontSize, f.FontColor, f.GroupName))
            .ToList();

        // The current step is the first not-yet-finished required recipient.
        var currentRecipientId = allRecipients
            .FirstOrDefault(r => r.IsRequired
                && r.Status != WorkflowStepStatus.Completed
                && r.Status != WorkflowStepStatus.Skipped
                && r.Status != WorkflowStepStatus.Rejected)?.Id;

        var hide = recipient.Document.HideRecipientIdentities;
        // Viewers aren't workflow gates — they don't belong in the signing-progress visualizer.
        var orderedForDisplay = allRecipients
            .Where(r => r.ApprovalType != ApprovalType.ViewOnly)
            .Select((r, idx) => (r, displayOrder: idx + 1))
            .ToList();

        var participants = orderedForDisplay
            .Select(x => new WorkflowParticipantDto(
                x.r.Order,
                hide && x.r.Id != recipient.Id ? $"Recipient {x.displayOrder}" : x.r.Name,
                x.r.Status,
                x.r.Id == currentRecipientId,
                x.r.Id == recipient.Id,
                x.r.SignedAt))
            .ToList();

        // It's my turn when no earlier-ordered required recipient is still outstanding.
        var isMyTurn = !allRecipients.Any(r =>
            r.Order < recipient.Order && r.IsRequired
            && r.Status != WorkflowStepStatus.Completed
            && r.Status != WorkflowStepStatus.Skipped);

        string? waitingForName = null;
        if (!isMyTurn && currentRecipientId.HasValue)
        {
            var waiting = orderedForDisplay.FirstOrDefault(x => x.r.Id == currentRecipientId);
            if (waiting.r is not null)
                waitingForName = hide ? $"Recipient {waiting.displayOrder}" : waiting.r.Name;
        }

        // Render HTML content for template-based documents (always from the snapshot taken at
        // request-creation time — unaffected by the source template being edited or deleted later)
        bool isHtmlTemplate = recipient.Document.HtmlTemplateId.HasValue;
        string? renderedHtml = null;
        if (isHtmlTemplate && recipient.Document.HtmlContentSnapshot is not null)
        {
            var filled = string.IsNullOrEmpty(recipient.Document.FilledValuesJson)
                ? []
                : JsonSerializer.Deserialize<Dictionary<string, string>>(recipient.Document.FilledValuesJson) ?? [];

            // Field types drive how a conditional clause compares its value (a Number field is
            // compared numerically, a Date chronologically). Absent — because the source template
            // was deleted — every field falls back to text and the contract still renders.
            var fieldTypes = await db.HtmlTemplateFields.AsNoTracking()
                .Where(f => f.HtmlTemplateId == recipient.Document.HtmlTemplateId!.Value)
                .ToDictionaryAsync(f => f.FieldKey, f => f.Type, cancellationToken);

            // A signature placeholder in the text becomes a slot showing who signs there and where they stand: the
            // viewer's own (to click), someone else's still to come, or already signed. The words are names and a
            // date only, so the slot reads the same in any language; the signing screen styles it by class.
            var signatureSlots = await BuildSignatureSlotsAsync(
                recipient.DocumentId, recipient.Id, hide, orderedForDisplay.ToDictionary(x => x.r.Id, x => x.displayOrder),
                allRecipients.ToDictionary(r => r.Id, r => r.Name), cancellationToken);

            renderedHtml = HtmlContractRenderer.Render(
                recipient.Document.HtmlContentSnapshot, filled, fieldTypes, signatureSlots);
        }

        return new SigningSessionDto(
            recipient.DocumentId,
            recipient.Document.Title,
            recipient.Document.FinalStoragePath ?? recipient.Document.StoragePath,
            recipient.Document.PageCount,
            recipient.Document.Message,
            new RecipientSessionDto(recipient.Id, recipient.Name, recipient.Email, recipient.ApprovalType, recipient.Status, recipient.RoleLabel,
                recipient.ChecklistItems.OrderBy(c => c.Order).Select(c => new ChecklistItemSessionDto(c.Id, c.Text, c.Order, c.IsChecked)).ToList()),
            fields,
            recipient.Status == WorkflowStepStatus.Completed,
            recipient.Status == WorkflowStepStatus.Rejected,
            recipient.Document.IsExpired(),
            isMyTurn,
            waitingForName,
            recipient.Document.ExpiresAt,
            recipient.Document.SlaHours,
            participants,
            isHtmlTemplate,
            renderedHtml,
            recipient.TokenExpiresAt,
            recipient.Document.Status,
            await BuildReviewGateAsync(recipient, cancellationToken));
    }

    private async Task<ReviewGateDto> BuildReviewGateAsync(Domain.Entities.Recipient recipient, CancellationToken cancellationToken)
    {
        var required = ReviewGateRules.Applies(recipient)
                       && recipient.Status is not (WorkflowStepStatus.Completed or WorkflowStepStatus.Rejected);
        var acknowledged = recipient.ReviewAcknowledgedAt is not null;

        // Findings about the contract are for the organisation's side: shown to participants who have an account.
        var email = recipient.Email.Trim().ToLower();
        var isMember = await db.Users.AsNoTracking().AnyAsync(u => u.Email != null && u.Email.ToLower() == email, cancellationToken);
        if (!required || !isMember)
            return new ReviewGateDto(required, acknowledged, ReviewRestricted: required, Review: null);

        var document = recipient.Document;
        if (document.ReadinessVerdict == ReadinessVerdict.NotEvaluated)
            return new ReviewGateDto(required, acknowledged, ReviewRestricted: false, Review: null);

        var items = await db.DocumentReadinessItems.AsNoTracking()
            .Where(i => i.DocumentId == document.Id)
            .ToListAsync(cancellationToken);

        var open = items.Where(i => i.Status == ReadinessItemStatus.Open).ToList();
        var review = new AiReviewDto(
            document.ReadinessVerdict,
            document.ReadinessScore,
            Documents.Queries.ReadinessDto.ScoreFrom(document.AiRiskAnalysisJson, "riskScore"),
            Documents.Queries.ReadinessDto.ScoreFrom(document.AiComplianceAnalysisJson, "complianceScore"),
            open.Count,
            open.Count(i => string.Equals(i.Severity, "Critical", StringComparison.OrdinalIgnoreCase)),
            items.Count - open.Count,
            items
                .OrderBy(i => i.Status == ReadinessItemStatus.Open ? 0 : 1)
                .ThenBy(i => Array.IndexOf(["Critical", "High", "Medium", "Low"], i.Severity))
                .Select(i => new AiReviewItemDto(i.Severity, i.Title, i.Detail, i.WhyItMatters, i.RecommendedAction, i.Status, i.ResolutionNote))
                .ToList());

        return new ReviewGateDto(required, acknowledged, ReviewRestricted: false, Review: review);
    }

    private async Task<Dictionary<string, string>> BuildSignatureSlotsAsync(
        Guid documentId,
        Guid viewerId,
        bool hideIdentities,
        IReadOnlyDictionary<Guid, int> displayOrder,
        IReadOnlyDictionary<Guid, string> names,
        CancellationToken cancellationToken)
    {
        var fields = await db.SignatureFields.AsNoTracking()
            .Where(f => f.DocumentId == documentId && f.TemplateKey != null)
            .ToListAsync(cancellationToken);

        var slots = new Dictionary<string, string>();
        foreach (var f in fields)
        {
            var name = f.RecipientId is { } rid && names.TryGetValue(rid, out var n)
                ? (hideIdentities && rid != viewerId
                    ? $"Recipient {(displayOrder.TryGetValue(rid, out var order) ? order : 0)}"
                    : n)
                : string.Empty;

            var (state, mark, extra) = f.IsSigned
                ? ("sig-signed", "&#10004;", f.SignedAt is { } at ? $"<span class=\"sig-date\">{at:yyyy-MM-dd}</span>" : string.Empty)
                : f.RecipientId == viewerId
                    ? ("sig-mine", "&#9998;", string.Empty)
                    : ("sig-waiting", "&#8987;", string.Empty);

            slots[f.TemplateKey!] =
                $"<span class=\"sig-slot {state}\" id=\"sig-slot-{f.Id}\"><span class=\"sig-mark\">{mark}</span>" +
                $"<span class=\"sig-name\">{System.Net.WebUtility.HtmlEncode(name)}</span>{extra}</span>";
        }

        return slots;
    }
}
