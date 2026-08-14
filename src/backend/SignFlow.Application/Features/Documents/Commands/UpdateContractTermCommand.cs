using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>
/// Records the negotiated contract term. Owner or administrator only — these dates drive the
/// lifecycle alerts, so they are entered deliberately rather than inferred from the text.
/// </summary>
public record UpdateContractTermCommand(
    Guid DocumentId,
    DateTime? StartDate,
    DateTime? EndDate,
    bool AutoRenews,
    int? NoticePeriodDays) : ICommand;

public class UpdateContractTermCommandValidator : AbstractValidator<UpdateContractTermCommand>
{
    public UpdateContractTermCommandValidator()
    {
        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate!.Value)
            .When(x => x.StartDate.HasValue && x.EndDate.HasValue)
            .WithMessage("The end date must be after the start date.");

        // A notice period longer than the term itself would put the deadline before the contract
        // started, which would fire the "notice deadline passed" alert on day one.
        RuleFor(x => x.NoticePeriodDays)
            .InclusiveBetween(0, 365)
            .When(x => x.NoticePeriodDays.HasValue)
            .WithMessage("The notice period must be between 0 and 365 days.");

        RuleFor(x => x.NoticePeriodDays)
            .Must((cmd, notice) => cmd.EndDate!.Value.Date.AddDays(-notice!.Value) > cmd.StartDate!.Value.Date)
            .When(x => x.StartDate.HasValue && x.EndDate.HasValue && x.NoticePeriodDays is > 0)
            .WithMessage("The notice period is longer than the contract term.");
    }
}

public class UpdateContractTermCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<UpdateContractTermCommandHandler> logger) : ICommandHandler<UpdateContractTermCommand>
{
    public async Task HandleAsync(UpdateContractTermCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        var isAdmin = currentUser.IsInRole("Administrator");
        if (!isAdmin && document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        // Re-arms the alert ladder against the new dates — see Document.SetContractTerm.
        document.SetContractTerm(
            command.StartDate, command.EndDate, command.AutoRenews, command.NoticePeriodDays);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Document {DocumentId} contract term set to {Start}–{End} by user {UserId}",
            command.DocumentId, command.StartDate, command.EndDate, currentUser.UserId);
    }
}
