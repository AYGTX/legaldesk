using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Comments.Commands;

/// <summary>Marks a top-level comment thread as resolved (or reopens it).</summary>
public record ResolveCommentCommand(Guid CommentId, bool Resolved) : ICommand;

public class ResolveCommentCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    INotificationHub hub) : ICommandHandler<ResolveCommentCommand>
{
    public async Task HandleAsync(ResolveCommentCommand command, CancellationToken cancellationToken = default)
    {
        var comment = await db.Comments
            .Include(c => c.Document)
            .FirstOrDefaultAsync(c => c.Id == command.CommentId && !c.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Comment), command.CommentId);

        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();
        var canResolve = currentUser.IsInRole("Administrator")
                         || comment.Document.OwnerId == userId
                         || comment.UserId == userId;
        if (!canResolve)
            throw new ForbiddenAccessException();

        if (command.Resolved) comment.Resolve();
        else comment.Unresolve();

        await db.SaveChangesAsync(cancellationToken);

        await hub.SendToGroupAsync(
            $"document-{comment.DocumentId}",
            "comments-changed",
            new { comment.DocumentId },
            cancellationToken);
    }
}
