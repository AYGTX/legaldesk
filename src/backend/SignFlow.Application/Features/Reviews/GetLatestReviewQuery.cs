using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Reviews;

/// <summary>The contract's review: latest run, verdict, scores and findings, without running anything.</summary>
public record GetLatestReviewQuery(Guid DocumentId) : IQuery<ReviewDto>;

public class GetLatestReviewQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetLatestReviewQuery, ReviewDto>
{
    public async Task<ReviewDto> HandleAsync(GetLatestReviewQuery query, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == query.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), query.DocumentId);

        // The legal team reads every contract's review; otherwise only its owner.
        if (!currentUser.Can(Permissions.ReadAllReviews) && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        return await ReviewView.BuildAsync(db, document, cancellationToken);
    }
}
