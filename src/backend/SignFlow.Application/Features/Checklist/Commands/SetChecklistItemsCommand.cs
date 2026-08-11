using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Checklist.Commands;

/// <summary>
/// Owner-side: defines the checklist a Review/Approval recipient must complete before they can
/// approve. Replaces the full list for that recipient (simplest editing model — matches how
/// ManageRecipientsCommand replaces recipients wholesale).
/// </summary>
public record SetChecklistItemsCommand(Guid DocumentId, Guid RecipientId, List<string> Items) : ICommand;

public class SetChecklistItemsCommandValidator : AbstractValidator<SetChecklistItemsCommand>
{
    public SetChecklistItemsCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.RecipientId).NotEmpty();
        RuleForEach(x => x.Items).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(20);
    }
}

public class SetChecklistItemsCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<SetChecklistItemsCommand>
{
    public async Task HandleAsync(SetChecklistItemsCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.FindAsync([command.DocumentId], cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        if (document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        var recipient = await db.Recipients
            .FirstOrDefaultAsync(r => r.Id == command.RecipientId && r.DocumentId == command.DocumentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Recipient), command.RecipientId);

        if (recipient.ApprovalType is not (ApprovalType.Review or ApprovalType.Approval))
            throw new ConflictException("Checklists only apply to Review and Approval recipients.");

        if (document.Status != DocumentStatus.Draft)
            throw new ConflictException("Checklists can only be edited while the document is a draft.");

        var existing = await db.ChecklistItems
            .Where(c => c.RecipientId == command.RecipientId)
            .ToListAsync(cancellationToken);
        db.ChecklistItems.RemoveRange(existing);

        for (var i = 0; i < command.Items.Count; i++)
            db.ChecklistItems.Add(ChecklistItem.Create(command.RecipientId, command.Items[i].Trim(), i + 1));

        await db.SaveChangesAsync(cancellationToken);
    }
}
