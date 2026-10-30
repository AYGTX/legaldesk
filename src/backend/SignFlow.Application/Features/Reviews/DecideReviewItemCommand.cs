using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Reviews;

/// <summary>
/// The owner's decision on one current finding (R-M8-4): Accepted (risk taken on purpose), NotApplicable (false
/// positive), or back to Open. Both decisions need a reason; a Critical needs a manager. The verdict is re-graded at
/// once, without re-reviewing. Fixed is set only by a review of a newer version.
/// </summary>
public record DecideReviewItemCommand(Guid DocumentId, Guid ItemId, ReadinessItemStatus Status, string? Reason)
    : ICommand<ReviewDto>;

public class DecideReviewItemCommandHandler(
    IApplicationDbContext db,
    IReviewQueue queue,
    ICurrentUserService currentUser,
    IAuditService auditService) : ICommandHandler<DecideReviewItemCommand, ReviewDto>
{
    public async Task<ReviewDto> HandleAsync(DecideReviewItemCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.ReadinessItems)
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        if (!currentUser.Can(Permissions.ManageAllContracts) && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        var item = document.ReadinessItems.FirstOrDefault(i => i.Id == command.ItemId)
            ?? throw new NotFoundException(nameof(DocumentReadinessItem), command.ItemId);

        if (command.Status == ReadinessItemStatus.Fixed)
            throw new ValidationException("A finding is marked fixed only by a review of a newer version.");

        if (document.Status != DocumentStatus.Draft)
            throw new ConflictException("review.locked", "The contract has been sent: its review can no longer change.");

        if (document.LastReviewRunId is null || item.LastSeenRunId != document.LastReviewRunId)
            throw new ConflictException("review.not_current", "Only findings of the latest review of this version can be decided.");

        // Taking a critical risk, or calling it a false positive, is a legal judgement: a manager decides (R-M8-4).
        if (command.Status != ReadinessItemStatus.Open
            && item.Severity == ReviewGrading.Critical
            && !currentUser.Can(Permissions.AcceptCriticalRisks))
            throw new ForbiddenAccessException("Only a manager or an administrator can accept a critical finding or set it aside.");

        var reason = command.Reason?.Trim();
        if (command.Status != ReadinessItemStatus.Open)
        {
            if (string.IsNullOrEmpty(reason))
                throw new ValidationException("Give a reason for this decision.");
            if (reason.Length > DocumentReadinessItem.MaxReasonLength)
                throw new ValidationException($"The reason is limited to {DocumentReadinessItem.MaxReasonLength} characters.");
        }

        item.Decide(command.Status, currentUser.UserId!.Value, reason);

        var grade = ReviewGrading.Grade(ReviewView.Current(document, document.ReadinessItems));
        document.SetReview(document.LastReviewRunId.Value, grade.Verdict, grade.ReadinessScore, grade.RiskScore);
        await db.SaveChangesAsync(cancellationToken);

        // A false positive goes to the lawyers who keep the grid, in the background and retried until delivered.
        if (command.Status == ReadinessItemStatus.NotApplicable && item.RuleKey is not null)
            queue.EnqueueFeedback(item.Id, item.OrganisationId);

        await auditService.LogAsync(
            document.Id,
            AuditAction.ReadinessItemUpdated,
            "system",
            $"Finding '{item.Title}' ({item.ClauseReference ?? "-"}, {item.Severity}) marked {command.Status}"
                + (reason is { Length: > 0 } ? $": {reason}" : string.Empty)
                + $"; verdict now {grade.Verdict}",
            currentUser.UserId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        return await ReviewView.BuildAsync(db, document, cancellationToken);
    }
}
