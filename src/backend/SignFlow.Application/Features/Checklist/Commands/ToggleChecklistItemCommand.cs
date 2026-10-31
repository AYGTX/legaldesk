using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Checklist.Commands;

/// <summary>Recipient-side: check/uncheck one of their own checklist items during review.</summary>
public record ToggleChecklistItemCommand(Guid RecipientId, Guid ItemId, bool Checked) : ICommand;

public class ToggleChecklistItemCommandValidator : AbstractValidator<ToggleChecklistItemCommand>
{
    public ToggleChecklistItemCommandValidator()
    {
        RuleFor(x => x.RecipientId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
    }
}

public class ToggleChecklistItemCommandHandler(IApplicationDbContext db) : ICommandHandler<ToggleChecklistItemCommand>
{
    public async Task HandleAsync(ToggleChecklistItemCommand command, CancellationToken cancellationToken = default)
    {
        var recipient = await db.Recipients
            .FirstOrDefaultAsync(r => r.Id == command.RecipientId, cancellationToken)
            ?? throw new NotFoundException("Signing session not found or has expired.");

        var item = await db.ChecklistItems
            .FirstOrDefaultAsync(c => c.Id == command.ItemId && c.RecipientId == recipient.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(ChecklistItem), command.ItemId);

        item.SetChecked(command.Checked);
        await db.SaveChangesAsync(cancellationToken);
    }
}
