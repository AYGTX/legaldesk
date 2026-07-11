using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Comments.Commands;

/// <summary>Soft-deletes a comment (and its replies when it is a top-level thread).</summary>
public record DeleteCommentCommand(Guid CommentId) : ICommand;

public class DeleteCommentCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    INotificationHub hub) : ICommandHandler<DeleteCommentCommand>
{
    public async Task HandleAsync(DeleteCommentCommand command, CancellationToken cancellationToken = default)
    {
        var comment = await db.Comments
            .Include(c => c.Document)
            .FirstOrDefaultAsync(c => c.Id == command.CommentId && !c.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Comment), command.CommentId);

        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();
        var canDelete = currentUser.IsInRole("Administrator")
                        || comment.Document.OwnerId == userId
                        || comment.UserId == userId;
        if (!canDelete)
            throw new ForbiddenAccessException();

        comment.SoftDelete();

        if (comment.ParentCommentId is null)
        {
            var replies = await db.Comments
                .Where(c => c.ParentCommentId == comment.Id && !c.IsDeleted)
                .ToListAsync(cancellationToken);
            foreach (var reply in replies)
                reply.SoftDelete();
        }

        await db.SaveChangesAsync(cancellationToken);

        await hub.SendToGroupAsync(
            $"document-{comment.DocumentId}",
            "comments-changed",
            new { comment.DocumentId },
            cancellationToken);
    }
}
