using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Recipients.Commands;

public record ManageRecipientsCommand(Guid DocumentId, WorkflowType WorkflowType, List<RecipientInputDto> Recipients, bool HideRecipientIdentities = false) : ICommand;

public record RecipientInputDto(
    Guid? Id,
    string Name,
    string Email,
    int Order,
    ApprovalType ApprovalType,
    string? PrivateMessage = null,
    string? Company = null);

public class ManageRecipientsCommandValidator : AbstractValidator<ManageRecipientsCommand>
{
    public ManageRecipientsCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.Recipients).NotNull().NotEmpty().WithMessage("At least one recipient is required.");
        RuleForEach(x => x.Recipients).ChildRules(r =>
        {
            r.RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            r.RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
            r.RuleFor(x => x.Order).GreaterThan(0);
        });
    }
}

public class ManageRecipientsCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<ManageRecipientsCommand>
{
    public async Task HandleAsync(ManageRecipientsCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.FindAsync([command.DocumentId], cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        if (document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        if (document.Status != DocumentStatus.Draft)
            throw new ConflictException("Recipients can only be managed on draft documents.");

        // Block recipients who are registered users currently on scheduled vacation.
        var today = DateTime.UtcNow.Date;
        var emails = command.Recipients
            .Select(r => r.Email.Trim().ToLower())
            .Distinct()
            .ToList();

        var unavailable = await db.Vacations
            .Where(v => v.StartDate <= today && v.EndDate >= today && emails.Contains(v.User.Email!.ToLower()))
            .Select(v => new { v.User.Email, v.EndDate })
            .ToListAsync(cancellationToken);

        if (unavailable.Count > 0)
        {
            var details = string.Join(", ", unavailable.Select(u => $"{u.Email} (available {u.EndDate.AddDays(1):yyyy-MM-dd})"));
            throw new Common.Exceptions.ValidationException(
                $"The following recipients are on vacation and cannot be added: {details}.");
        }

        var existingIds = command.Recipients.Where(r => r.Id.HasValue).Select(r => r.Id!.Value).ToHashSet();
        var toDelete = await db.Recipients
            .Where(r => r.DocumentId == command.DocumentId && !existingIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        db.Recipients.RemoveRange(toDelete);
        document.SetWorkflowType(command.WorkflowType);
        document.SetHideRecipientIdentities(command.HideRecipientIdentities);

        foreach (var dto in command.Recipients)
        {
            if (dto.Id.HasValue)
            {
                var existing = await db.Recipients.FindAsync([dto.Id.Value], cancellationToken);
                if (existing is not null) continue;
            }

            var recipient = Recipient.Create(command.DocumentId, dto.Name, dto.Email, dto.Order, dto.ApprovalType, dto.PrivateMessage, dto.Company, dto.Id);
            db.Recipients.Add(recipient);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
