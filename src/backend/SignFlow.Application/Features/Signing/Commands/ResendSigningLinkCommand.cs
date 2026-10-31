using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Signing.Commands;

/// <summary>
/// The owner sends a participant a new signing link (lost e-mail, expired link). Links are never shown to anyone
/// (R-M5-1), so this replaces "copy the link": the new token goes in the e-mail only and the old link stops working.
/// </summary>
public record ResendSigningLinkCommand(Guid DocumentId, Guid RecipientId) : ICommand;

public class ResendSigningLinkCommandHandler(
    IApplicationDbContext db,
    IEmailService email,
    ICurrentUserService currentUser,
    IAuditService audit) : ICommandHandler<ResendSigningLinkCommand>
{
    public async Task HandleAsync(ResendSigningLinkCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.Recipients)
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        if (!currentUser.Can(Permissions.ManageAllContracts) && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        var recipient = document.Recipients.FirstOrDefault(r => r.Id == command.RecipientId)
            ?? throw new NotFoundException(nameof(Recipient), command.RecipientId);

        if (document.Status != DocumentStatus.InProgress)
            throw new ConflictException("link.not_sendable", "Links are sent only while the contract is out for signature.");
        if (recipient.TokenHash is null)
            throw new ConflictException("link.not_sendable", "This participant has not been asked yet: their link goes out when their turn comes.");
        if (recipient.Status is WorkflowStepStatus.Completed or WorkflowStepStatus.Rejected)
            throw new ConflictException("link.not_sendable", "This participant has already responded.");

        var token = SecureTokens.Generate();
        recipient.IssueLink(SecureTokens.Hash(token));
        await db.SaveChangesAsync(cancellationToken);

        await email.SendTemplateAsync(recipient.Email, "DocumentReceived",
            new { recipient.Name, DocumentTitle = document.Title, SigningLink = $"/sign/{token}" },
            "Your link to the contract", cancellationToken);

        await audit.LogAsync(document.Id, AuditAction.EmailSent, "system",
            $"New signing link sent to {recipient.Name} ({recipient.Email}); the previous link no longer works",
            currentUser.UserId, actorEmail: currentUser.UserEmail, cancellationToken: cancellationToken);
    }
}
