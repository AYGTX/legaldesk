using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
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
    DocumentStatus DocumentStatus = DocumentStatus.InProgress);

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
            var html = recipient.Document.HtmlContentSnapshot;
            if (!string.IsNullOrEmpty(recipient.Document.FilledValuesJson))
            {
                var filled = JsonSerializer.Deserialize<Dictionary<string, string>>(recipient.Document.FilledValuesJson)
                             ?? [];
                html = Regex.Replace(html, @"\{\{(\w+)\}\}", m =>
                    filled.TryGetValue(m.Groups[1].Value, out var v) ? System.Net.WebUtility.HtmlEncode(v) : m.Value);
            }
            // Remove any remaining {{placeholders}} (e.g. approver signature fields, auto-fill keys)
            renderedHtml = Regex.Replace(html, @"\{\{\w+\}\}", string.Empty);
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
            recipient.Document.Status);
    }
}
