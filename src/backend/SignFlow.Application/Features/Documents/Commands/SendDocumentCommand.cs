using FluentValidation;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>
/// Starts the validation circuit. <paramref name="Force"/> is the owner acknowledging a "needs
/// work" pre-flight verdict — a critical finding is never forceable.
/// </summary>
public record SendDocumentCommand(Guid DocumentId, bool Force = false) : ICommand;

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

        EnforceReadinessGate(document, command.Force);

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

    /// <summary>
    /// Applies the pre-flight verdict. Contracts that were never reviewed pass through untouched, so
    /// this does not change the behaviour of any flow that predates the review feature.
    ///
    /// The error is keyed on "readiness" rather than being a bare message, so the UI can tell this
    /// apart from an ordinary validation failure and offer to acknowledge and retry.
    /// </summary>
    private static void EnforceReadinessGate(Document document, bool force)
    {
        switch (document.ReadinessVerdict)
        {
            case ReadinessVerdict.Blocked:
                throw new Common.Exceptions.ValidationException(
                    [new FluentValidation.Results.ValidationFailure("readiness",
                        "This contract has unresolved critical issues. Resolve or dismiss them in the pre-flight review before sending.")]);

            case ReadinessVerdict.NeedsWork when !force:
                throw new Common.Exceptions.ValidationException(
                    [new FluentValidation.Results.ValidationFailure("readinessWarning",
                        "The pre-flight review flagged issues on this contract. Confirm to send it anyway.")]);
        }
    }
}
