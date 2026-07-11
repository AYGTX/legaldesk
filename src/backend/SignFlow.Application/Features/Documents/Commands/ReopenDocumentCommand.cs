using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>Administrator action to reopen an expired request, restarting its SLA clock from now.</summary>
public record ReopenDocumentCommand(Guid DocumentId, int? SlaHours = null) : ICommand;

public class ReopenDocumentCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<ReopenDocumentCommandHandler> logger) : ICommandHandler<ReopenDocumentCommand>
{
    public async Task HandleAsync(ReopenDocumentCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator"))
            throw new ForbiddenAccessException();

        var document = await db.Documents
            .IgnoreQueryFilters()
            .Include(d => d.Workflow)
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        if (document.Status is not (DocumentStatus.Expired or DocumentStatus.Cancelled))
            throw new ConflictException("Only expired or cancelled requests can be reopened.");

        document.Reopen(command.SlaHours);
        document.Workflow?.Start();

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            document.Id,
            AuditAction.DocumentSent,
            "system",
            $"Request reopened by administrator (new deadline {document.ExpiresAt:u})",
            currentUser.UserId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        logger.LogInformation("Document {DocumentId} reopened by admin {UserId}", command.DocumentId, currentUser.UserId);
    }
}
