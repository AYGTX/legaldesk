using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.RegulatoryWatch.Commands;

public record UpdateRegulatoryTextCommand(
    Guid RegulatoryTextId,
    string Title,
    RegulatoryCategory Category,
    RegulatoryJurisdiction Jurisdiction,
    string Summary,
    string Keywords,
    string Language,
    string? Reference,
    DateTime? EffectiveDate,
    Guid? RequestTypeId) : ICommand;

public class UpdateRegulatoryTextCommandValidator : AbstractValidator<UpdateRegulatoryTextCommand>
{
    public UpdateRegulatoryTextCommandValidator()
    {
        RuleFor(x => x.RegulatoryTextId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Summary).NotEmpty();
        RuleFor(x => x.Keywords).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Reference).MaximumLength(300);
        RuleFor(x => x.Language).NotEmpty().Must(l => l is "en" or "fr")
            .WithMessage("Language must be 'en' or 'fr'.");
    }
}

public class UpdateRegulatoryTextCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<UpdateRegulatoryTextCommandHandler> logger) : ICommandHandler<UpdateRegulatoryTextCommand>
{
    public async Task HandleAsync(UpdateRegulatoryTextCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator") && !currentUser.IsInRole("Manager"))
            throw new ForbiddenAccessException();

        var text = await db.RegulatoryTexts.FirstOrDefaultAsync(r => r.Id == command.RegulatoryTextId, cancellationToken)
            ?? throw new NotFoundException(nameof(RegulatoryText), command.RegulatoryTextId);

        text.Update(
            command.Title,
            command.Category,
            command.Jurisdiction,
            command.Summary,
            command.Keywords,
            command.Language,
            command.Reference,
            command.EffectiveDate,
            command.RequestTypeId);

        text.SetUpdatedBy(currentUser.UserId!.Value.ToString());
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Regulatory text {RegulatoryTextId} updated by {UserId}", text.Id, currentUser.UserId);
    }
}
