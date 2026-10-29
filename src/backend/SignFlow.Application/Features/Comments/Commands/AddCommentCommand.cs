using SignFlow.Application.Common.Security;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using ValidationException = SignFlow.Application.Common.Exceptions.ValidationException;

namespace SignFlow.Application.Features.Comments.Commands;

/// <summary>Adds a comment (or a reply) to a document as the authenticated user.</summary>
public record AddCommentCommand(
    Guid DocumentId,
    string Content,
    Guid? ParentCommentId,
    int? Page,
    float? X,
    float? Y,
    string IpAddress) : ICommand<CommentDto>;

public class AddCommentCommandValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).When(x => x.Page.HasValue);
        RuleFor(x => x.X).InclusiveBetween(0, 100).When(x => x.X.HasValue);
        RuleFor(x => x.Y).InclusiveBetween(0, 100).When(x => x.Y.HasValue);
    }
}

public class AddCommentCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IAuditService auditService,
    INotificationService notifications,
    INotificationHub hub,
    ILogger<AddCommentCommandHandler> logger) : ICommandHandler<AddCommentCommand, CommentDto>
{
    public async Task<CommentDto> HandleAsync(AddCommentCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.Recipients)
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();
        var isPrivileged = currentUser.Can(Permissions.ManageAllContracts) || document.OwnerId == userId;
        if (!isPrivileged && !document.Recipients.Any(r => r.Email == currentUser.UserEmail))
            throw new ForbiddenAccessException();

        Comment? parent = null;
        if (command.ParentCommentId.HasValue)
        {
            parent = await db.Comments
                .FirstOrDefaultAsync(c => c.Id == command.ParentCommentId.Value && !c.IsDeleted, cancellationToken)
                ?? throw new NotFoundException(nameof(Comment), command.ParentCommentId.Value);

            if (parent.DocumentId != document.Id)
                throw new ValidationException("The parent comment belongs to a different document.");
            if (parent.ParentCommentId is not null)
                throw new ValidationException("Replies can only be added to top-level comments.");
        }

        var author = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new ForbiddenAccessException();

        var comment = Comment.Create(
            document.Id,
            command.Content,
            userId: userId,
            parentId: command.ParentCommentId,
            authorName: author.FullName);

        if (command.Page.HasValue)
            comment.SetPosition(command.Page.Value, command.X ?? 0, command.Y ?? 0);

        db.Comments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            document.Id,
            AuditAction.CommentAdded,
            command.IpAddress,
            comment.ParentCommentId is null ? "Comment added" : "Reply added",
            userId: userId,
            actorName: author.FullName,
            actorEmail: author.Email,
            cancellationToken: cancellationToken);

        await CommentNotifier.NotifyAsync(
            db, notifications, hub, document, comment,
            authorUserId: userId, authorEmail: author.Email, authorName: author.FullName,
            parent, cancellationToken);

        logger.LogInformation("Comment {CommentId} added to document {DocumentId} by user {UserId}", comment.Id, document.Id, userId);

        return CommentMapping.ToDto(comment, author.AvatarUrl, isMine: true, anonymize: false);
    }
}

/// <summary>
/// Shared comment notification fan-out: document owner, parent-comment author on replies,
/// and any registered user whose email is @-mentioned in the content — each at most once,
/// never the author themself. Also pings the document's SignalR group so open views refetch
/// (a content-free ping by design: anonymization is per-viewer, so pushing the DTO could
/// leak hidden identities on HideRecipientIdentities documents).
/// </summary>
public static class CommentNotifier
{
    private static readonly Regex MentionEmailRegex = new(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.Compiled);

    public static async Task NotifyAsync(
        IApplicationDbContext db,
        INotificationService notifications,
        INotificationHub hub,
        Document document,
        Comment comment,
        Guid? authorUserId,
        string? authorEmail,
        string authorName,
        Comment? parent,
        CancellationToken cancellationToken)
    {
        var notifiedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(authorEmail))
            notifiedEmails.Add(authorEmail);

        var snippet = comment.Content.Length > 120 ? comment.Content[..120] + "…" : comment.Content;

        if (authorUserId != document.OwnerId)
        {
            var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == document.OwnerId, cancellationToken);
            if (owner is not null && notifiedEmails.Add(owner.Email!))
            {
                await notifications.NotifyUserAsync(
                    document.OwnerId,
                    NotificationType.CommentMention,
                    "New comment",
                    $"{authorName} commented on \"{document.Title}\": {snippet}",
                    document.Id,
                    cancellationToken);
            }
        }

        if (parent is not null)
        {
            var parentEmail = parent.RecipientEmail;
            if (parent.UserId.HasValue && parentEmail is null)
                parentEmail = await db.Users.AsNoTracking()
                    .Where(u => u.Id == parent.UserId.Value)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrEmpty(parentEmail) && notifiedEmails.Add(parentEmail))
            {
                await notifications.NotifyByEmailAsync(
                    parentEmail,
                    NotificationType.CommentMention,
                    "New reply to your comment",
                    $"{authorName} replied to your comment on \"{document.Title}\": {snippet}",
                    document.Id,
                    cancellationToken);
            }
        }

        foreach (Match match in MentionEmailRegex.Matches(comment.Content))
        {
            if (!notifiedEmails.Add(match.Value)) continue;

            await notifications.NotifyByEmailAsync(
                match.Value,
                NotificationType.CommentMention,
                "You were mentioned in a comment",
                $"{authorName} mentioned you on \"{document.Title}\": {snippet}",
                document.Id,
                cancellationToken);
        }

        await hub.SendToGroupAsync(
            $"document-{document.Id}",
            "comments-changed",
            new { DocumentId = document.Id },
            cancellationToken);
    }
}
