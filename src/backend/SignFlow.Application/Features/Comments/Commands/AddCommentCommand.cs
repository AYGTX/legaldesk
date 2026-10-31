using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using ValidationException = SignFlow.Application.Common.Exceptions.ValidationException;

namespace SignFlow.Application.Features.Comments.Commands;

/// <summary>Adds a comment (or a reply) to a document as the authenticated user (R-M7-1). People of the organisation
/// choose who reads a new thread; a participant's thread is always shared with the participants. Mentions are picked
/// people: user ids and participant ids.</summary>
public record AddCommentCommand(
    Guid DocumentId,
    string Content,
    Guid? ParentCommentId,
    int? Page,
    float? X,
    float? Y,
    string IpAddress,
    CommentVisibility Visibility = CommentVisibility.Internal,
    List<Guid>? MentionIds = null) : ICommand<CommentDto>;

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

        // A participant never writes an internal note; a reply belongs to its thread.
        var visibility = parent?.Visibility ?? (isPrivileged ? command.Visibility : CommentVisibility.Participants);
        if (!isPrivileged && visibility == CommentVisibility.Internal)
            throw new ForbiddenAccessException();
        comment.Place(visibility, document.Version);
        comment.Mention(await CommentNotifier.CheckMentionsAsync(db, document, visibility, command.MentionIds, cancellationToken));

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
/// and the people picked as mentioned — each at most once, never the author themself, and
/// never a participant for an internal note. Also pings the document's SignalR group so open views refetch
/// (a content-free ping by design: anonymization is per-viewer, so pushing the DTO could
/// leak hidden identities on HideRecipientIdentities documents).
/// </summary>
public static class CommentNotifier
{
    /// <summary>Mentions are people of the organisation or the contract's participants; a participant only in a thread
    /// they can read.</summary>
    public static async Task<List<Guid>> CheckMentionsAsync(IApplicationDbContext db, Document document, CommentVisibility visibility,
        IReadOnlyCollection<Guid>? ids, CancellationToken ct)
    {
        if (ids is not { Count: > 0 }) return [];
        if (ids.Count > 20) throw new ValidationException("At most 20 people can be mentioned.");
        var users = await db.Users.Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
        var participants = document.Recipients.Where(r => ids.Contains(r.Id) && !r.IsPool).Select(r => r.Id).ToList();
        if (users.Count + participants.Count != ids.Distinct().Count())
            throw new ValidationException("Only people of the organisation or participants of this contract can be mentioned.");
        if (visibility == CommentVisibility.Internal && participants.Count > 0)
            throw new ValidationException("A participant cannot be mentioned in an internal note.");
        return [.. users, .. participants];
    }

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

            var participantAuthor = parent.RecipientEmail is not null;
            if (!string.IsNullOrEmpty(parentEmail) && !(participantAuthor && comment.Visibility == CommentVisibility.Internal)
                && notifiedEmails.Add(parentEmail))
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

        var mentioned = comment.Mentioned;
        if (mentioned.Count > 0)
        {
            var users = await db.Users.AsNoTracking().Where(u => mentioned.Contains(u.Id)).ToListAsync(cancellationToken);
            foreach (var user in users.Where(u => notifiedEmails.Add(u.Email!)))
                await notifications.NotifyUserAsync(user.Id, NotificationType.CommentMention, "You were mentioned in a comment",
                    $"{authorName} mentioned you on \"{document.Title}\": {snippet}", document.Id, cancellationToken);

            var participants = await db.Recipients.AsNoTracking().Where(r => mentioned.Contains(r.Id)).ToListAsync(cancellationToken);
            foreach (var participant in participants.Where(p => notifiedEmails.Add(p.Email)))
                await notifications.NotifyByEmailAsync(participant.Email, NotificationType.CommentMention, "You were mentioned in a comment",
                    $"{authorName} mentioned you on \"{document.Title}\": {snippet}", document.Id, cancellationToken);
        }

        await hub.SendToGroupAsync(
            $"document-{document.Id}",
            "comments-changed",
            new { DocumentId = document.Id },
            cancellationToken);
    }
}
