using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Signing.Commands;

/// <summary>
/// Non-terminal counterpart of RejectDocumentCommand for reviewers and approvers: pauses the
/// workflow in ChangesRequested so the owner can upload a revision and re-run the approval
/// round, instead of killing the request outright.
/// </summary>
public record RequestChangesCommand(string SecureToken, string Reason, string IpAddress) : ICommand;

public class RequestChangesCommandValidator : AbstractValidator<RequestChangesCommand>
{
    public RequestChangesCommandValidator()
    {
        RuleFor(x => x.SecureToken).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}

public class RequestChangesCommandHandler(
    IApplicationDbContext db,
    IWorkflowEngine workflowEngine,
    IAuditService auditService,
    INotificationHub hub) : ICommandHandler<RequestChangesCommand>
{
    public async Task HandleAsync(RequestChangesCommand command, CancellationToken cancellationToken = default)
    {
        var recipient = await db.Recipients
            .Include(r => r.Document)
            .FirstOrDefaultAsync(r => r.SecureToken == command.SecureToken, cancellationToken)
            ?? throw new NotFoundException("Signing session not found.");

        if (recipient.Status == WorkflowStepStatus.Completed)
            throw new ConflictException("You have already completed this document.");

        if (recipient.Status == WorkflowStepStatus.Rejected)
            throw new ConflictException("You have already rejected this document.");

        if (recipient.Status == WorkflowStepStatus.ChangesRequested)
            throw new ConflictException("You have already requested changes on this document.");

        if (recipient.ApprovalType is not (ApprovalType.Review or ApprovalType.Approval))
            throw new ConflictException("Only reviewers and approvers can request changes. Signers can decline instead.");

        if (recipient.Document.Status is not (DocumentStatus.InProgress or DocumentStatus.Pending))
            throw new ConflictException("This document is no longer awaiting action.");

        await workflowEngine.RequestChangesAsync(recipient.DocumentId, recipient.Id, command.Reason, cancellationToken);

        // The reason also lands in the shared discussion thread so the revision conversation
        // has full context. Owner notification is already handled by the engine — no
        // CommentNotifier here, or the owner would be notified twice.
        var linkedUser = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == recipient.Email, cancellationToken);

        var comment = Comment.Create(
            recipient.DocumentId,
            command.Reason,
            userId: linkedUser?.Id,
            recipientEmail: recipient.Email,
            parentId: null,
            authorName: recipient.Name);

        db.Comments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);

        await hub.SendToGroupAsync(
            $"document-{recipient.DocumentId}",
            "comments-changed",
            new { DocumentId = recipient.DocumentId },
            cancellationToken);

        await auditService.LogAsync(
            recipient.DocumentId,
            AuditAction.ChangesRequested,
            command.IpAddress,
            $"Changes requested: {command.Reason}",
            recipientId: recipient.Id,
            actorName: recipient.Name,
            actorEmail: recipient.Email,
            cancellationToken: cancellationToken);
    }
}
