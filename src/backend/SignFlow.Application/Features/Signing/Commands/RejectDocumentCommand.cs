using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Signing.Commands;

public record RejectDocumentCommand(Guid RecipientId, string Reason) : ICommand;

public class RejectDocumentCommandValidator : AbstractValidator<RejectDocumentCommand>
{
    public RejectDocumentCommandValidator()
    {
        RuleFor(x => x.RecipientId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class RejectDocumentCommandHandler(
    IApplicationDbContext db,
    IWorkflowEngine workflowEngine,
    IAuditService auditService) : ICommandHandler<RejectDocumentCommand>
{
    public async Task HandleAsync(RejectDocumentCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new Common.Exceptions.ValidationException("A reason is required to decline a document.");

        var recipient = await db.Recipients
            .Include(r => r.Document)
            .FirstOrDefaultAsync(r => r.Id == command.RecipientId, cancellationToken)
            ?? throw new NotFoundException("Signing session not found.");

        // Recipient.Status never becomes InProgress (only WorkflowStep.Status does), so the old
        // guard `Status != InProgress` rejected every decline attempt with a 409.
        if (recipient.Status == WorkflowStepStatus.Completed)
            throw new ConflictException("You have already completed this document.");

        if (recipient.Status == WorkflowStepStatus.Rejected)
            throw new ConflictException("You have already rejected this document.");

        if (recipient.ApprovalType == ApprovalType.ViewOnly)
            throw new ConflictException("View-only participants can't decline documents.");

        if (recipient.Document.Status is not (DocumentStatus.InProgress or DocumentStatus.Pending))
            throw new ConflictException("This document is no longer awaiting action.");

        await workflowEngine.RejectAsync(recipient.DocumentId, recipient.Id, command.Reason, cancellationToken);

        await auditService.LogAsync(
            recipient.DocumentId,
            AuditAction.DocumentRejected,
            "system",
            $"Document rejected: {command.Reason}",
            recipientId: recipient.Id,
            actorName: recipient.Name,
            actorEmail: recipient.Email,
            cancellationToken: cancellationToken);
    }
}
