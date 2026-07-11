using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Services.Background;

public class WorkflowEngine(
    IApplicationDbContext db,
    IEmailService emailService,
    INotificationHub hub,
    INotificationService notifications,
    ILogger<WorkflowEngine> logger) : IWorkflowEngine
{
    public async Task StartAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.Recipients.OrderBy(r => r.Order))
            .Include(d => d.Workflow)
                .ThenInclude(w => w.Steps.OrderBy(s => s.Order))
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document {documentId} not found.");

        document.SetStatus(DocumentStatus.InProgress);

        if (document.Workflow is null)
        {
            var workflowType = document.WorkflowType;
            var workflow = Workflow.Create(documentId, workflowType);
            db.Workflows.Add(workflow);

            document = await db.Documents
                .Include(d => d.Recipients.OrderBy(r => r.Order))
                .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)!;

            // ViewOnly participants are not part of the gating workflow: no steps are created
            // for them. They get notified once at start and keep view/comment access via their
            // token for the document's whole lifetime.
            var recipients = document.Recipients
                .Where(r => r.ApprovalType != ApprovalType.ViewOnly)
                .OrderBy(r => r.Order)
                .ToList();

            if (workflowType == WorkflowType.Parallel)
            {
                // All recipients sign simultaneously in one group
                foreach (var r in recipients)
                {
                    var step = WorkflowStep.Create(workflow.Id, 1, r.Name, r.ApprovalType, r.Id, isParallel: true);
                    step.SetParallelGroup(1);
                    db.WorkflowSteps.Add(step);
                }
            }
            else
            {
                // Sequential: each recipient is its own stage.
                // Mixed: recipients with the same Order share a parallel stage.
                var stages = recipients.GroupBy(r => r.Order).OrderBy(g => g.Key).ToList();
                int stageOrder = 1;
                foreach (var stage in stages)
                {
                    bool isParallelStage = workflowType == WorkflowType.Mixed && stage.Count() > 1;
                    foreach (var r in stage)
                    {
                        var step = WorkflowStep.Create(workflow.Id, stageOrder, r.Name, r.ApprovalType, r.Id, isParallel: isParallelStage);
                        if (isParallelStage) step.SetParallelGroup(stageOrder);
                        db.WorkflowSteps.Add(step);
                    }
                    stageOrder++;
                }
            }

            foreach (var viewer in document.Recipients.Where(r => r.ApprovalType == ApprovalType.ViewOnly))
            {
                await emailService.SendTemplateAsync(
                    viewer.Email,
                    "DocumentReceived",
                    new { viewer.Name, DocumentTitle = document.Title, SigningLink = $"/sign/{viewer.SecureToken}" },
                    "A document was shared with you",
                    cancellationToken);

                await notifications.NotifyByEmailAsync(
                    viewer.Email,
                    NotificationType.DocumentReceived,
                    "A document was shared with you",
                    $"You have view access to \"{document.Title}\".",
                    documentId,
                    cancellationToken);
            }

            workflow.Start();
            await db.SaveChangesAsync(cancellationToken);

            document = await db.Documents
                .Include(d => d.Workflow)
                    .ThenInclude(w => w.Steps.OrderBy(s => s.Order))
                .Include(d => d.Recipients.OrderBy(r => r.Order))
                .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)!;
        }
        else
        {
            document.Workflow.Start();
            await db.SaveChangesAsync(cancellationToken);
        }

        await ActivateNextStepAsync(documentId, cancellationToken);
    }

    public async Task AdvanceAsync(Guid documentId, Guid recipientId, CancellationToken cancellationToken = default)
    {
        var recipient = await db.Recipients.FindAsync([recipientId], cancellationToken);
        if (recipient is null) return;

        recipient.MarkSigned("system", "workflow-engine");
        await db.SaveChangesAsync(cancellationToken);

        var workflow = await db.Workflows
            .Include(w => w.Steps.OrderBy(s => s.Order))
            .FirstOrDefaultAsync(w => w.DocumentId == documentId, cancellationToken);

        if (workflow is null) return;

        var currentStep = workflow.Steps.FirstOrDefault(s => s.RecipientId == recipientId);
        currentStep?.Complete();
        await db.SaveChangesAsync(cancellationToken);

        // Notify the owner of signing progress (role-aware wording)
        var document = await db.Documents.FindAsync([documentId], cancellationToken);
        if (document is not null)
        {
            var verb = recipient.ApprovalType switch
            {
                ApprovalType.Review => "reviewed and approved",
                ApprovalType.Approval => "approved",
                _ => "signed"
            };
            await notifications.NotifyUserAsync(
                document.OwnerId,
                NotificationType.Completed,
                recipient.ApprovalType is ApprovalType.Review or ApprovalType.Approval
                    ? "A participant approved"
                    : "A recipient signed",
                $"{recipient.Name} {verb} \"{document.Title}\".",
                documentId,
                cancellationToken);
        }

        if (await IsCompleteAsync(documentId, cancellationToken))
        {
            await CompleteDocumentAsync(documentId, cancellationToken);
        }
        else
        {
            // For parallel steps only advance when the entire group has completed
            bool shouldAdvance;
            if (currentStep is { IsParallel: true, ParallelGroupId: not null })
            {
                shouldAdvance = workflow.Steps
                    .Where(s => s.ParallelGroupId == currentStep.ParallelGroupId)
                    .All(s => s.Status == WorkflowStepStatus.Completed);
            }
            else
            {
                shouldAdvance = true;
            }

            if (shouldAdvance)
            {
                workflow.AdvanceToNextStep();
                await db.SaveChangesAsync(cancellationToken);
                await ActivateNextStepAsync(documentId, cancellationToken);
            }
        }
    }

    public async Task RejectAsync(Guid documentId, Guid recipientId, string reason, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.Recipients)
            .Include(d => d.Workflow)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null) return;

        var recipient = document.Recipients.FirstOrDefault(r => r.Id == recipientId);
        recipient?.Reject(reason);
        document.Workflow?.Reject();
        document.SetStatus(DocumentStatus.Rejected);

        await db.SaveChangesAsync(cancellationToken);

        // Notify document owner
        var owner = await db.Users.FindAsync([document.OwnerId], cancellationToken);
        if (owner is not null)
        {
            await emailService.SendTemplateAsync(owner.Email!, "Rejected", new { document.Title, Reason = reason, RecipientName = recipient?.Name }, cancellationToken: cancellationToken);
        }

        await notifications.NotifyUserAsync(
            document.OwnerId,
            NotificationType.Rejected,
            "Request declined",
            $"{recipient?.Name ?? "A recipient"} declined to sign \"{document.Title}\"" + (string.IsNullOrWhiteSpace(reason) ? "." : $": {reason}"),
            documentId,
            cancellationToken);

        logger.LogInformation("Document {DocumentId} rejected by recipient {RecipientId}", documentId, recipientId);
    }

    public async Task CheckExpirationAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.FindAsync([documentId], cancellationToken);
        if (document is null || !document.IsExpired()) return;

        if (document.Status == DocumentStatus.InProgress)
        {
            document.SetStatus(DocumentStatus.Expired);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Document {DocumentId} expired", documentId);
        }
    }

    public async Task<bool> IsCompleteAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var allRecipientsDone = await db.Recipients
            .Where(r => r.DocumentId == documentId && r.IsRequired)
            .AllAsync(r => r.Status == WorkflowStepStatus.Completed, cancellationToken);

        return allRecipientsDone;
    }

    public async Task<Recipient?> GetCurrentRecipientAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return await db.Recipients
            .Where(r => r.DocumentId == documentId && r.Status == WorkflowStepStatus.InProgress)
            .OrderBy(r => r.Order)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task ActivateNextStepAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var workflow = await db.Workflows
            .Include(w => w.Steps.OrderBy(s => s.Order))
            .FirstOrDefaultAsync(w => w.DocumentId == documentId, cancellationToken);

        if (workflow is null) return;

        var nextStep = workflow.Steps.FirstOrDefault(s => s.Status == WorkflowStepStatus.Pending);
        if (nextStep is null) return;

        // Activate all steps at the same stage order — this handles parallel groups naturally
        var stepsToActivate = workflow.Steps
            .Where(s => s.Order == nextStep.Order && s.Status == WorkflowStepStatus.Pending)
            .ToList();

        var docTitle = (await db.Documents.FindAsync([documentId], cancellationToken))?.Title ?? "a document";

        foreach (var step in stepsToActivate)
        {
            step.Start();

            var recipient = await db.Recipients.FindAsync([step.RecipientId], cancellationToken);
            if (recipient is not null)
            {
                recipient.MarkViewed();

                var (emailSubject, turnTitle, turnBody) = recipient.ApprovalType switch
                {
                    ApprovalType.Review => ("Please review your document", "It's your turn to review",
                        $"\"{docTitle}\" is waiting for your review."),
                    ApprovalType.Approval => ("Your approval is requested", "It's your turn to approve",
                        $"\"{docTitle}\" is waiting for your approval."),
                    _ => ("Please sign your document", "It's your turn to sign",
                        $"\"{docTitle}\" is waiting for your signature.")
                };

                await emailService.SendTemplateAsync(
                    recipient.Email,
                    "DocumentReceived",
                    new { recipient.Name, DocumentTitle = docTitle, SigningLink = $"/sign/{recipient.SecureToken}" },
                    emailSubject,
                    cancellationToken);

                await notifications.NotifyByEmailAsync(
                    recipient.Email,
                    NotificationType.DocumentReceived,
                    turnTitle,
                    turnBody,
                    documentId,
                    cancellationToken);
            }

            logger.LogInformation("Activated signing for recipient {RecipientId} on document {DocumentId}", step.RecipientId, documentId);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await db.Documents
            .Include(d => d.Workflow)
            .Include(d => d.Owner)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null) return;

        document.SetStatus(DocumentStatus.Completed);
        document.Workflow?.Complete();
        await db.SaveChangesAsync(cancellationToken);

        await emailService.SendTemplateAsync(
            document.Owner.Email!,
            "Completed",
            new { document.Title },
            "Document signed by all parties",
            cancellationToken);

        await hub.SendToGroupAsync(
            $"document-{documentId}",
            "document-completed",
            new { DocumentId = documentId },
            cancellationToken);

        await notifications.NotifyUserAsync(
            document.OwnerId,
            NotificationType.WorkflowFinished,
            "Request completed",
            $"\"{document.Title}\" has been signed by all parties.",
            documentId,
            cancellationToken);

        logger.LogInformation("Document {DocumentId} completed", documentId);
    }
}
