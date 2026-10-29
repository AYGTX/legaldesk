using SignFlow.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Comments.Queries;

/// <summary>Lists a document's comments for the authenticated viewer (owner, admin, or recipient).</summary>
public record GetDocumentCommentsQuery(Guid DocumentId) : IQuery<List<CommentDto>>;

public class GetDocumentCommentsQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetDocumentCommentsQuery, List<CommentDto>>
{
    public async Task<List<CommentDto>> HandleAsync(GetDocumentCommentsQuery query, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.AsNoTracking()
            .Include(d => d.Recipients)
            .FirstOrDefaultAsync(d => d.Id == query.DocumentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), query.DocumentId);

        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();
        var isPrivileged = currentUser.Can(Permissions.ManageAllContracts) || document.OwnerId == userId;
        if (!isPrivileged && !document.Recipients.Any(r => r.Email == currentUser.UserEmail))
            throw new ForbiddenAccessException();

        var rows = await (
                from c in db.Comments
                where c.DocumentId == query.DocumentId && !c.IsDeleted
                join u in db.Users on c.UserId equals u.Id into users
                from u in users.DefaultIfEmpty()
                orderby c.CreatedAt
                select new { Comment = c, AvatarUrl = u != null ? u.AvatarUrl : null })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.Select(r =>
        {
            var isMine = r.Comment.UserId == userId;
            var anonymize = CommentMapping.ShouldAnonymize(
                r.Comment, document.OwnerId, isPrivileged, isMine, document.HideRecipientIdentities);
            return CommentMapping.ToDto(r.Comment, r.AvatarUrl, isMine, anonymize);
        }).ToList();
    }
}
