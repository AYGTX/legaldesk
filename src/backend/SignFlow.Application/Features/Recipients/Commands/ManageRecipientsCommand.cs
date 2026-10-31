using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Recipients.Commands;

public record ManageRecipientsCommand(Guid DocumentId, WorkflowType WorkflowType, List<RecipientInputDto> Recipients, bool HideRecipientIdentities = false) : ICommand;

/// <summary>One participant of the circuit (M4).</summary>
/// <param name="StageRule">All: the stage waits for everyone sharing this order; Any: the first answer closes it (R-M4-2).</param>
/// <param name="PoolTeamId">A team step: its members share it, any one acts (R-M4-3). Name and e-mail are then ignored.</param>
/// <param name="PoolRole">A role step, like a team step.</param>
/// <param name="DueInWorkingDays">Due this many working days after the step opens (R-M4-10).</param>
/// <param name="ReminderEveryDays">Reminder interval while the step is open; 0 turns reminders off (R-M4-6).</param>
/// <param name="EscalateAfterDays">Days after which the escalation contact (else the owner) is told.</param>
public record RecipientInputDto(
    Guid? Id,
    string Name,
    string Email,
    int Order,
    ApprovalType ApprovalType,
    string? PrivateMessage = null,
    string? Company = null,
    string? RoleLabel = null,
    StageRule StageRule = StageRule.All,
    Guid? PoolTeamId = null,
    string? PoolRole = null,
    int? DueInWorkingDays = null,
    int ReminderEveryDays = 3,
    int? EscalateAfterDays = null,
    string? EscalationEmail = null)
{
    public bool IsPool => PoolTeamId is not null || !string.IsNullOrWhiteSpace(PoolRole);
}

public class ManageRecipientsCommandValidator : AbstractValidator<ManageRecipientsCommand>
{
    public ManageRecipientsCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.Recipients).NotNull().NotEmpty().WithMessage("At least one recipient is required.");
        RuleForEach(x => x.Recipients).ChildRules(r =>
        {
            r.RuleFor(x => x.Name).NotEmpty().MaximumLength(200).When(x => !x.IsPool);
            r.RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256).When(x => !x.IsPool);
            r.RuleFor(x => x.Order).GreaterThan(0);
            r.RuleFor(x => x.RoleLabel).MaximumLength(100);
            r.RuleFor(x => x.ApprovalType).NotEqual(ApprovalType.Reject).WithMessage("\"Reject\" is not a step of a circuit.");
            r.RuleFor(x => x.DueInWorkingDays).InclusiveBetween(1, 60).When(x => x.DueInWorkingDays is not null);
            r.RuleFor(x => x.ReminderEveryDays).InclusiveBetween(0, 30);
            r.RuleFor(x => x.EscalateAfterDays).InclusiveBetween(1, 90).When(x => x.EscalateAfterDays is not null);
            r.RuleFor(x => x.EscalationEmail).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.EscalationEmail));
        });
    }
}

public class ManageRecipientsCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ICircuitDirectory? directory = null) : ICommandHandler<ManageRecipientsCommand>
{
    public async Task HandleAsync(ManageRecipientsCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.FindAsync([command.DocumentId], cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        if (document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        if (document.Status != DocumentStatus.Draft)
            throw new ConflictException("Recipients can only be managed on draft documents.");

        var inputs = new List<RecipientInputDto>(command.Recipients.Count);
        foreach (var dto in command.Recipients)
            inputs.Add(await NormaliseAsync(dto, cancellationToken));

        // Someone away blocks the circuit unless they named a colleague to act for them (R-M1-8).
        var today = DateTime.UtcNow.Date;
        var emails = inputs.Where(r => !r.IsPool).Select(r => r.Email.Trim().ToLower()).Distinct().ToList();

        var unavailable = await db.Vacations
            .Where(v => v.StartDate <= today && v.EndDate >= today && v.DelegateUserId == null && emails.Contains(v.User.Email!.ToLower()))
            .Select(v => new { v.User.Email, v.EndDate })
            .ToListAsync(cancellationToken);

        if (unavailable.Count > 0)
        {
            var details = string.Join(", ", unavailable.Select(u => $"{u.Email} (available {u.EndDate.AddDays(1):yyyy-MM-dd})"));
            throw new Common.Exceptions.ValidationException(
                $"The following recipients are on vacation and cannot be added: {details}.");
        }

        var current = await db.Recipients.Where(r => r.DocumentId == command.DocumentId).ToListAsync(cancellationToken);
        var currentIds = current.Select(r => r.Id).ToList();
        // Who acted in an earlier round keeps that history: their identity is fixed, and leaving the circuit retires them.
        var withHistory = (await db.Signatures.Where(s => currentIds.Contains(s.RecipientId)).Select(s => s.RecipientId).ToListAsync(cancellationToken))
            .Concat(await db.WorkflowSteps.Where(s => currentIds.Contains(s.RecipientId)).Select(s => s.RecipientId).ToListAsync(cancellationToken))
            .ToHashSet();

        var keptIds = inputs.Where(r => r.Id.HasValue).Select(r => r.Id!.Value).ToHashSet();
        // Stand-ins and retired participants are history, not part of what the editor sends back.
        foreach (var gone in current.Where(r => !keptIds.Contains(r.Id) && r.DelegatedFromRecipientId is null && r.RetiredAt is null))
        {
            if (withHistory.Contains(gone.Id)) gone.Retire();
            else db.Recipients.Remove(gone);
        }

        document.SetWorkflowType(command.WorkflowType);
        document.SetHideRecipientIdentities(command.HideRecipientIdentities);

        foreach (var dto in inputs)
        {
            var existing = dto.Id is { } id ? current.FirstOrDefault(r => r.Id == id) : null;
            if (existing is not null)
            {
                existing.Revise(dto.Name, dto.Email, dto.Order, dto.ApprovalType, dto.PrivateMessage, dto.Company, dto.RoleLabel,
                    identityLocked: withHistory.Contains(existing.Id));
            }
            else
            {
                existing = Recipient.Create(command.DocumentId, dto.Name, dto.Email, dto.Order, dto.ApprovalType, dto.PrivateMessage, dto.Company, dto.Id, dto.RoleLabel);
                db.Recipients.Add(existing);
            }
            existing.SetCircuit(dto.StageRule, dto.PoolTeamId, dto.PoolTeamId is null ? dto.PoolRole : null, dto.DueInWorkingDays,
                dto.ReminderEveryDays, dto.EscalateAfterDays, dto.EscalationEmail);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A team or role step is named after its group and written to its members, who must exist.</summary>
    private async Task<RecipientInputDto> NormaliseAsync(RecipientInputDto dto, CancellationToken ct)
    {
        if (dto.ApprovalType == ApprovalType.Reject)
            throw new Common.Exceptions.ValidationException("\"Reject\" is not a step of a circuit.");
        if (!dto.IsPool) return dto;

        if (dto.ApprovalType == ApprovalType.ViewOnly)
            throw new Common.Exceptions.ValidationException("A team or role can act on a contract, not only view it.");

        string name;
        if (dto.PoolTeamId is { } teamId)
        {
            name = await db.Teams.Where(t => t.Id == teamId).Select(t => t.Name).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException(nameof(Team), teamId);
        }
        else
        {
            name = dto.PoolRole!.Trim();
        }

        if (directory is not null && (await directory.PoolMembersAsync(dto.PoolTeamId, dto.PoolTeamId is null ? name : null, ct)).Count == 0)
            throw new ConflictException("circuit.poolEmpty", $"\"{name}\" has no active member, so nobody could act for it.",
                new Dictionary<string, object?> { ["team"] = name });

        return dto with { Name = name, Email = Recipient.PoolAddress };
    }
}
