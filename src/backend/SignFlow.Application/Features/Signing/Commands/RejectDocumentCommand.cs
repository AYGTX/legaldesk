using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Signing.Commands;

public record RejectDocumentCommand(string SecureToken, string Reason) : ICommand;

public class RejectDocumentCommandValidator : AbstractValidator<RejectDocumentCommand>
{
    public RejectDocumentCommandValidator()
    {
        RuleFor(x => x.SecureToken).NotEmpty();
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
        var recipient = await db.Recipients
            .Include(r => r.Document)
            .FirstOrDefaultAsync(r => r.SecureToken == command.SecureToken, cancellationToken)
            ?? throw new NotFoundException("Signing session not found.");

        if (recipient.Status != WorkflowStepStatus.InProgress)
            throw new ConflictException("Cannot reject a document that is not awaiting your action.");

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
