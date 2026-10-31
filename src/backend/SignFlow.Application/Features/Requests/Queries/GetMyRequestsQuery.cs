using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Requests.Queries;

public record GetMyRequestsQuery : IQuery<MyRequestsDto>;

public record MyRequestsDto(
    List<SentRequestDto> Sent,
    List<ReceivedRequestDto> Received);

public record SentRequestDto(
    Guid DocumentId,
    string Title,
    DocumentStatus Status,
    WorkflowType? WorkflowType,
    DateTime CreatedAt,
    DateTime? SentAt,
    DateTime? CompletedAt,
    DateTime? ExpiresAt,
    int TotalRecipients,
    int CompletedRecipients,
    string? CurrentRecipientName,
    List<RecipientProgressDto> Recipients);

public record RecipientProgressDto(
    string Name,
    string Email,
    int Order,
    WorkflowStepStatus Status,
    bool IsCurrent,
    DateTime? SignedAt,
    DateTime? ViewedAt);

public record ReceivedRequestDto(
    Guid DocumentId,
    string Title,
    string OwnerName,
    DocumentStatus DocumentStatus,
    WorkflowStepStatus MyStatus,
    int MyOrder,
    bool IsMyTurn,
    Guid RecipientId,
    DateTime? ReceivedAt,
    DateTime? ExpiresAt,
    DateTime? SignedAt,
    ApprovalType ApprovalType,
    string? RoleLabel);

public class GetMyRequestsQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetMyRequestsQuery, MyRequestsDto>
{
    public async Task<MyRequestsDto> HandleAsync(GetMyRequestsQuery query, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();
        var email = currentUser.UserEmail ?? throw new ForbiddenAccessException();

        // ---- Workflows I initiated (sent) ----
        var sentDocs = await db.Documents.AsNoTracking()
            .Include(d => d.Recipients)
            .Include(d => d.Workflow)
                .ThenInclude(w => w.Steps)
            .Where(d => !d.IsDeleted && !d.IsTemplate && d.OwnerId == userId && d.Status != DocumentStatus.Draft)
            .OrderByDescending(d => d.SentAt ?? d.CreatedAt)
            .ToListAsync(cancellationToken);

        var sent = sentDocs.Select(d =>
        {
            var currentRecipientId = d.Workflow?.Steps
                .FirstOrDefault(s => s.Status == WorkflowStepStatus.InProgress)?.RecipientId;

            var recipients = d.Recipients
                .OrderBy(r => r.Order)
                .Select(r => new RecipientProgressDto(
                    r.Name, r.Email, r.Order, r.Status,
                    r.Id == currentRecipientId, r.SignedAt, r.ViewedAt))
                .ToList();

            return new SentRequestDto(
                d.Id,
                d.Title,
                d.Status,
                d.Workflow?.Type,
                d.CreatedAt,
                d.SentAt,
                d.CompletedAt,
                d.ExpiresAt,
                recipients.Count,
                recipients.Count(r => r.Status == WorkflowStepStatus.Completed),
                recipients.FirstOrDefault(r => r.IsCurrent)?.Name,
                recipients);
        }).ToList();

        // ---- Requests sent to me ----
        var myRecipientRecords = await db.Recipients.AsNoTrackingWithIdentityResolution()
            .Include(r => r.Document).ThenInclude(d => d.Owner)
            .Include(r => r.Document).ThenInclude(d => d.Recipients)
            .Include(r => r.Document).ThenInclude(d => d.Workflow)
                .ThenInclude(w => w.Steps)
            .Where(r => r.Email == email && !r.Document.IsDeleted && r.Document.Status != DocumentStatus.Draft)
            .OrderByDescending(r => r.Document.SentAt ?? r.Document.CreatedAt)
            .ToListAsync(cancellationToken);

        var received = myRecipientRecords.Select(r =>
        {
            var doc = r.Document;
            var isActive = doc.Status is DocumentStatus.InProgress or DocumentStatus.Pending or DocumentStatus.WaitingForOthers;
            var unfinished = r.Status is WorkflowStepStatus.Pending or WorkflowStepStatus.InProgress;
            bool isMyTurn;

            if (!isActive || !unfinished)
            {
                isMyTurn = false;
            }
            else if (doc.Workflow?.Steps.Any() == true)
            {
                // Check this recipient's own step — handles both sequential (one InProgress)
                // and parallel (multiple InProgress simultaneously) workflows correctly.
                isMyTurn = doc.Workflow.Steps.Any(s =>
                    s.RecipientId == r.Id && s.Status == WorkflowStepStatus.InProgress);
            }
            else
            {
                // No workflow entity — fall back to order-based check.
                isMyTurn = !doc.Recipients.Any(other =>
                    other.Order < r.Order &&
                    other.Status is WorkflowStepStatus.Pending or WorkflowStepStatus.InProgress);
            }

            return new ReceivedRequestDto(
                doc.Id,
                doc.Title,
                doc.Owner.FullName,
                doc.Status,
                r.Status,
                r.Order,
                isMyTurn,
                r.Id,
                doc.SentAt,
                r.TokenExpiresAt ?? doc.ExpiresAt,
                r.SignedAt,
                r.ApprovalType,
                r.RoleLabel);
        }).ToList();

        return new MyRequestsDto(sent, received);
    }
}
