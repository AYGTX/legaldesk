using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Signing.Commands;

/// <summary>A participant hands their open step to someone else, with a note (R-M4-8). Their own link stops working;
/// the stand-in gets one, the step's due date and its fields.</summary>
public record DelegateStepCommand(Guid RecipientId, string Name, string Email, string Note, string IpAddress) : ICommand<DelegatedStepDto>;

public record DelegatedStepDto(Guid RecipientId, string Name, string Email);

public class DelegateStepCommandValidator : AbstractValidator<DelegateStepCommand>
{
    public DelegateStepCommandValidator()
    {
        RuleFor(x => x.RecipientId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Note).NotEmpty().MaximumLength(1000);
    }
}

public class DelegateStepCommandHandler(
    IApplicationDbContext db,
    IWorkflowEngine workflowEngine,
    IAuditService auditService) : ICommandHandler<DelegateStepCommand, DelegatedStepDto>
{
    public async Task<DelegatedStepDto> HandleAsync(DelegateStepCommand command, CancellationToken cancellationToken = default)
    {
        var recipient = await db.Recipients.FirstOrDefaultAsync(r => r.Id == command.RecipientId, cancellationToken)
            ?? throw new NotFoundException("Signing session not found.");

        if (recipient.ApprovalType == ApprovalType.ViewOnly)
            throw new ConflictException("View-only participants have no step to hand over.");
        if (recipient.IsPool)
            throw new ConflictException("delegate.pool", "A team or role step is already shared by its members; it cannot be handed over.");

        var stand = await workflowEngine.DelegateAsync(recipient.DocumentId, recipient.Id, command.Name, command.Email, command.Note.Trim(), cancellationToken);

        await auditService.LogAsync(
            recipient.DocumentId,
            AuditAction.DelegationAdded,
            command.IpAddress,
            $"{recipient.Name} handed their step to {stand.Name} ({stand.Email}): {command.Note.Trim()}",
            recipientId: recipient.Id,
            actorName: recipient.Name,
            actorEmail: recipient.Email,
            cancellationToken: cancellationToken);

        return new DelegatedStepDto(stand.Id, stand.Name, stand.Email);
    }
}
