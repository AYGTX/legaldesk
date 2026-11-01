using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Queries;

public record GetDashboardQuery : IQuery<DashboardDto>;

public record DashboardDto(
    int PendingSignatures,
    int WaitingForOthers,
    int Completed,
    int Rejected,
    int Expired,
    int Drafts,
    int TotalDocuments,
    List<RecentActivityDto> RecentActivity,
    List<DocumentSummaryDto> PendingDocuments,
    int ChangesRequested = 0,
    /// <summary>The user's own executed contracts whose term ends within 30 days.</summary>
    int ExpiringSoon = 0);

public record RecentActivityDto(Guid DocumentId, string DocumentTitle, AuditAction Action, string? ActorName, DateTime Timestamp, string? Description);

public class GetDashboardQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetDashboardQuery, DashboardDto>
{
    public async Task<DashboardDto> HandleAsync(GetDashboardQuery query, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId!.Value;
        var email = currentUser.UserEmail!;

        var ownedDocs = db.Documents.AsNoTracking().Where(d => !d.IsDeleted && !d.IsTemplate && d.OwnerId == userId);
        var recipientDocs = db.Documents.AsNoTracking()
            .Where(d => !d.IsDeleted && d.Recipients.Any(r => r.Email == email));

        var pendingSignatures = await recipientDocs
            .CountAsync(d => d.Status == DocumentStatus.InProgress &&
                d.Recipients.Any(r => r.Email == email && r.Status == WorkflowStepStatus.InProgress), cancellationToken);

        var waitingForOthers = await ownedDocs
            .CountAsync(d => d.Status == DocumentStatus.InProgress, cancellationToken);

        var completed = await ownedDocs.CountAsync(d => d.Status == DocumentStatus.Completed, cancellationToken);
        var rejected = await ownedDocs.CountAsync(d => d.Status == DocumentStatus.Rejected, cancellationToken);
        var expired = await ownedDocs.CountAsync(d => d.Status == DocumentStatus.Expired, cancellationToken);
        var drafts = await ownedDocs.CountAsync(d => d.Status == DocumentStatus.Draft, cancellationToken);
        var changesRequested = await ownedDocs.CountAsync(d => d.Status == DocumentStatus.ChangesRequested, cancellationToken);
        var total = await ownedDocs.CountAsync(cancellationToken);

        // Executed contracts of this user's whose term runs out inside the "expiring soon" window.
        var soonCutoff = DateTime.UtcNow.Date.AddDays(30);
        var expiringSoon = await ownedDocs.CountAsync(
            d => d.Status == DocumentStatus.Completed
                 && d.Term != null && d.Term.CurrentEndDate != null
                 && d.Term.CurrentEndDate >= DateTime.UtcNow.Date
                 && d.Term.CurrentEndDate <= soonCutoff,
            cancellationToken);

        var recentActivity = await db.AuditLogs.AsNoTracking()
            .Where(a => db.Documents.Any(d => d.Id == a.DocumentId && d.OwnerId == userId))
            .OrderByDescending(a => a.Timestamp)
            .Take(20)
            .Select(a => new RecentActivityDto(a.DocumentId, a.Document.Title, a.Action, a.ActorName, a.Timestamp, a.Description))
            .ToListAsync(cancellationToken);

        var pendingDocuments = await recipientDocs
            .Where(d => d.Status == DocumentStatus.InProgress)
            .OrderByDescending(d => d.SentAt)
            .Take(10)
            .Select(d => new DocumentSummaryDto(
                d.Id, d.Title, d.Status, d.FileName, d.FileSizeBytes, d.PageCount,
                d.Recipients.Count,
                d.Recipients.Count(r => r.Status == WorkflowStepStatus.Completed),
                d.CreatedAt, d.SentAt, d.CompletedAt, d.ExpiresAt,
                d.Owner.FullName, d.Owner.Email, d.Tags,
                d.RequestType != null ? d.RequestType.Name : null))
            .ToListAsync(cancellationToken);

        return new DashboardDto(pendingSignatures, waitingForOthers, completed, rejected, expired, drafts, total, recentActivity, pendingDocuments, changesRequested, expiringSoon);
    }
}
