using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Reviews;

/// <summary>Starts a Harvey review of the contract's current version in the background (R-M8-9).</summary>
public record StartReviewCommand(Guid DocumentId) : ICommand<ReviewRunDto>;

public class StartReviewCommandHandler(
    IApplicationDbContext db,
    ILegalCorpusClient harvey,
    IReviewQueue queue,
    ICurrentUserService currentUser,
    IAuditService auditService) : ICommandHandler<StartReviewCommand, ReviewRunDto>
{
    /// <summary>A run still "running" after this long died with its server; a new review may start.</summary>
    public static readonly TimeSpan Abandoned = TimeSpan.FromMinutes(10);

    public async Task<ReviewRunDto> HandleAsync(StartReviewCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        if (!currentUser.Can(Permissions.ManageAllContracts) && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        // One review at a time per contract: asking again while one runs returns it.
        var active = await db.ReviewRuns
            .Where(r => r.DocumentId == document.Id
                        && (r.Status == ReviewRunStatus.Queued || r.Status == ReviewRunStatus.Running))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (active is not null)
        {
            if (DateTime.UtcNow - active.CreatedAt < Abandoned)
                return ReviewRunDto.From(active);
            active.Fail(ReviewRun.Unavailable, "The review stopped without an answer.");
        }

        // Refused at once when Harvey is down, so nothing waits in the background for an answer that cannot come.
        if (!await harvey.IsAvailableAsync(cancellationToken))
            throw new ServiceUnavailableException(ReviewRun.Unavailable,
                "Harvey cannot be reached: the contract cannot be reviewed now. Try again in a few minutes.");

        // The law and the rules are read at the signature date when the contract is signed, else today.
        var asOf = document.Status == DocumentStatus.Completed && document.CompletedAt is { } signed ? signed : DateTime.UtcNow;
        var run = ReviewRun.Queue(document.Id, document.Version, currentUser.UserId, asOf);
        db.ReviewRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        queue.EnqueueReview(run.Id, run.OrganisationId);

        await auditService.LogAsync(
            document.Id,
            AuditAction.ReviewRequested,
            "system",
            $"Harvey review requested for version {document.Version}",
            currentUser.UserId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        return ReviewRunDto.From(run);
    }
}
