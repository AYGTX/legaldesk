using FluentValidation;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

public record SendDocumentCommand(Guid DocumentId) : ICommand;

public class SendDocumentCommandValidator : AbstractValidator<SendDocumentCommand>
{
    public SendDocumentCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
    }
}

public class SendDocumentCommandHandler(
    IApplicationDbContext db,
    IWorkflowEngine workflowEngine,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<SendDocumentCommandHandler> logger) : ICommandHandler<SendDocumentCommand>
{
    public async Task HandleAsync(SendDocumentCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.FindAsync([command.DocumentId], cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        if (document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        if (document.Status != DocumentStatus.Draft)
            throw new ConflictException("Only draft documents can be sent.");

        if (!db.Recipients.Any(r => r.DocumentId == document.Id))
            throw new ConflictException("Document must have at least one recipient before sending.");

        await workflowEngine.StartAsync(document.Id, cancellationToken);

        await auditService.LogAsync(
            document.Id,
            AuditAction.DocumentSent,
            "system",
            "Document sent for signing",
            currentUser.UserId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        logger.LogInformation("Document {DocumentId} sent for signing", command.DocumentId);
    }
}
