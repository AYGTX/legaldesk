using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using ValidationException = SignFlow.Application.Common.Exceptions.ValidationException;

namespace SignFlow.Application.Features.Comments.Commands;

/// <summary>
/// Adds a comment (or a reply) as a signing-session participant identified by their secure
/// token — works for external signers with no account, same as the rest of the signing flow.
/// </summary>
public record AddSigningCommentCommand(
    Guid RecipientId,
    string Content,
    Guid? ParentCommentId,
    int? Page,
    float? X,
    float? Y,
    string IpAddress) : ICommand<CommentDto>;

public class AddSigningCommentCommandValidator : AbstractValidator<AddSigningCommentCommand>
{
    public AddSigningCommentCommandValidator()
    {
        RuleFor(x => x.RecipientId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).When(x => x.Page.HasValue);
        RuleFor(x => x.X).InclusiveBetween(0, 100).When(x => x.X.HasValue);
        RuleFor(x => x.Y).InclusiveBetween(0, 100).When(x => x.Y.HasValue);
    }
}

public class AddSigningCommentCommandHandler(
    IApplicationDbContext db,
    IAuditService auditService,
    INotificationService notifications,
    INotificationHub hub,
    ILogger<AddSigningCommentCommandHandler> logger) : ICommandHandler<AddSigningCommentCommand, CommentDto>
{
    public async Task<CommentDto> HandleAsync(AddSigningCommentCommand command, CancellationToken cancellationToken = default)
    {
        var recipient = await db.Recipients
            .Include(r => r.Document)
            .FirstOrDefaultAsync(r => r.Id == command.RecipientId, cancellationToken)
            ?? throw new NotFoundException("Signing session not found or has expired.");

        var document = recipient.Document;

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

        // If the recipient's email matches a registered account, link the comment to it so
        // their avatar shows and their notifications thread correctly.
        var linkedUser = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == recipient.Email, cancellationToken);

        var comment = Comment.Create(
            document.Id,
            command.Content,
            userId: linkedUser?.Id,
            recipientEmail: recipient.Email,
            parentId: command.ParentCommentId,
            authorName: recipient.Name);

        if (command.Page.HasValue)
            comment.SetPosition(command.Page.Value, command.X ?? 0, command.Y ?? 0);

        db.Comments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            document.Id,
            AuditAction.CommentAdded,
            command.IpAddress,
            comment.ParentCommentId is null ? "Comment added" : "Reply added",
            recipientId: recipient.Id,
            actorName: recipient.Name,
            actorEmail: recipient.Email,
            cancellationToken: cancellationToken);

        await CommentNotifier.NotifyAsync(
            db, notifications, hub, document, comment,
            authorUserId: linkedUser?.Id, authorEmail: recipient.Email, authorName: recipient.Name,
            parent, cancellationToken);

        logger.LogInformation("Comment {CommentId} added to document {DocumentId} by recipient {RecipientId}", comment.Id, document.Id, recipient.Id);

        return CommentMapping.ToDto(comment, linkedUser?.AvatarUrl, isMine: true, anonymize: false);
    }
}
