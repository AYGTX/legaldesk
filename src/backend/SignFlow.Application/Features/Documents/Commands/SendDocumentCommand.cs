using FluentValidation;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>
/// Starts the validation circuit, behind the review gate (R-M8-8). <paramref name="Reason"/> is the owner's reason for
/// sending with an open High finding; it is kept in the audit trail. A Critical finding is never forceable.
/// </summary>
public record SendDocumentCommand(Guid DocumentId, string? Reason = null) : ICommand;

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

        var reason = EnforceReviewGate(document, command.Reason);

        await workflowEngine.StartAsync(document.Id, cancellationToken);

        await auditService.LogAsync(
            document.Id,
            AuditAction.DocumentSent,
            "system",
            "Document sent for signing",
            currentUser.UserId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        if (reason is not null)
            await auditService.LogAsync(
                document.Id,
                AuditAction.SentWithOpenRisks,
                "system",
                $"Sent with open high-risk findings. Reason: {reason}",
                currentUser.UserId,
                actorEmail: currentUser.UserEmail,
                cancellationToken: cancellationToken);

        logger.LogInformation("Document {DocumentId} sent for signing", command.DocumentId);
    }

    /// <summary>
    /// The review gate, the same for every contract (R-M8-8): no completed review of this version → refused; an open
    /// Critical → refused, never forceable; an open High → the owner gives a reason (returned, for the audit trail).
    /// Each error is keyed so the page can tell which one it is and offer the right next step.
    /// </summary>
    private static string? EnforceReviewGate(Document document, string? reason)
    {
        if (document.LastReviewRunId is null || document.ReadinessVerdict == ReadinessVerdict.NotEvaluated)
            throw Refused("readinessRequired", "Run Harvey's review of this version before sending it.");

        switch (document.ReadinessVerdict)
        {
            case ReadinessVerdict.Blocked:
                throw Refused("readiness",
                    "This contract has open critical findings. Fix them, or have a manager accept them, before sending.");

            case ReadinessVerdict.NeedsWork when string.IsNullOrWhiteSpace(reason):
                throw Refused("readinessWarning",
                    "The review found high risks that are still open. Give your reason to send the contract anyway.");

            case ReadinessVerdict.NeedsWork:
                return reason!.Trim().Length > 1000 ? reason.Trim()[..1000] : reason.Trim();

            default:
                return null;
        }
    }

    private static Common.Exceptions.ValidationException Refused(string key, string message) =>
        new([new FluentValidation.Results.ValidationFailure(key, message)]);
}
