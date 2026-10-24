using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Signing.Commands;

/// <summary>
/// The participant confirms they have read the document and the AI review shown to them. Recorded with the time and
/// what the review said at that moment, so the trail shows what they had in front of them when they acted.
/// </summary>
public record AcknowledgeReviewCommand(string SecureToken, string IpAddress, string UserAgent) : ICommand;

public class AcknowledgeReviewCommandHandler(
    IApplicationDbContext db,
    IAuditService auditService) : ICommandHandler<AcknowledgeReviewCommand>
{
    public async Task HandleAsync(AcknowledgeReviewCommand command, CancellationToken cancellationToken = default)
    {
        var recipient = await db.Recipients
            .Include(r => r.Document)
            .FirstOrDefaultAsync(r => r.SecureToken == command.SecureToken, cancellationToken)
            ?? throw new NotFoundException("Signing session not found or has expired.");

        if (recipient.Status is WorkflowStepStatus.Completed or WorkflowStepStatus.Rejected)
            throw new ConflictException("You have already responded to this request.");

        if (!ReviewGateRules.Applies(recipient) || recipient.ReviewAcknowledgedAt is not null)
            return;

        recipient.AcknowledgeReview();
        await db.SaveChangesAsync(cancellationToken);

        var document = recipient.Document;
        await auditService.LogAsync(
            recipient.DocumentId,
            AuditAction.ReviewAcknowledged,
            command.IpAddress,
            $"Confirmed the document and its Harvey review were read (review verdict: {document.ReadinessVerdict}" +
                (document.ReadinessScore is { } score ? $", readiness {score}/100" : string.Empty) + ")",
            recipientId: recipient.Id,
            userAgent: command.UserAgent,
            actorName: recipient.Name,
            actorEmail: recipient.Email,
            cancellationToken: cancellationToken);
    }
}
