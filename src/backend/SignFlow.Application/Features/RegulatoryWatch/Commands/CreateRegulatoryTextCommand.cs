using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.RegulatoryWatch.Commands;

public record CreateRegulatoryTextCommand(
    string Title,
    RegulatoryCategory Category,
    RegulatoryJurisdiction Jurisdiction,
    string Summary,
    string Keywords,
    string Language,
    string? Reference,
    DateTime? EffectiveDate,
    Guid? RequestTypeId,
    string? SourceUrl = null) : ICommand<Guid>;

public class CreateRegulatoryTextCommandValidator : AbstractValidator<CreateRegulatoryTextCommand>
{
    public CreateRegulatoryTextCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Summary).NotEmpty();
        RuleFor(x => x.Keywords).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Reference).MaximumLength(300);
        RuleFor(x => x.Language).NotEmpty().Must(l => l is "en" or "fr" or "ar")
            .WithMessage("Language must be 'en', 'fr' or 'ar'.");
        RuleFor(x => x.SourceUrl).MaximumLength(500)
            .Must(u => string.IsNullOrWhiteSpace(u) || (Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            .WithMessage("The source must be a web address (http or https).");
    }
}

public class CreateRegulatoryTextCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<CreateRegulatoryTextCommandHandler> logger) : ICommandHandler<CreateRegulatoryTextCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateRegulatoryTextCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.EditRegulatoryTexts))
            throw new ForbiddenAccessException();

        var userId = currentUser.UserId!.Value;

        var text = RegulatoryText.Create(
            command.Title,
            command.Category,
            command.Jurisdiction,
            command.Summary,
            command.Keywords,
            userId,
            command.Language,
            command.Reference,
            command.EffectiveDate,
            command.RequestTypeId,
            command.SourceUrl);

        text.SetCreatedBy(userId.ToString());
        db.RegulatoryTexts.Add(text);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Regulatory text '{Title}' created by {UserId}", command.Title, userId);
        return text.Id;
    }
}
