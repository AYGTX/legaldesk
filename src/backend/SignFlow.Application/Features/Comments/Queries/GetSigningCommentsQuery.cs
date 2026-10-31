using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Comments.Queries;

/// <summary>Lists a document's comments for a signing-session participant identified by token.</summary>
public record GetSigningCommentsQuery(Guid RecipientId) : IQuery<List<CommentDto>>;

public class GetSigningCommentsQueryHandler(
    IApplicationDbContext db) : IQueryHandler<GetSigningCommentsQuery, List<CommentDto>>
{
    public async Task<List<CommentDto>> HandleAsync(GetSigningCommentsQuery query, CancellationToken cancellationToken = default)
    {
        var recipient = await db.Recipients.AsNoTracking()
            .Include(r => r.Document)
            .FirstOrDefaultAsync(r => r.Id == query.RecipientId, cancellationToken)
            ?? throw new NotFoundException("Signing session not found or has expired.");

        var document = recipient.Document;

        var rows = await (
                from c in db.Comments.Where(x => x.Visibility == CommentVisibility.Participants)
                where c.DocumentId == document.Id && !c.IsDeleted
                join u in db.Users on c.UserId equals u.Id into users
                from u in users.DefaultIfEmpty()
                orderby c.CreatedAt
                select new { Comment = c, AvatarUrl = u != null ? u.AvatarUrl : null, UserEmail = u != null ? u.Email : null })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.Select(r =>
        {
            // A comment is "mine" whether it was posted through this signing token or through
            // the logged-in app by the same person (matched by email).
            var isMine = r.Comment.RecipientEmail == recipient.Email || r.UserEmail == recipient.Email;
            var anonymize = CommentMapping.ShouldAnonymize(
                r.Comment, document.OwnerId, viewerIsPrivileged: false, isMine, document.HideRecipientIdentities);
            return CommentMapping.ToDto(r.Comment, r.AvatarUrl, isMine, anonymize);
        }).ToList();
    }
}
