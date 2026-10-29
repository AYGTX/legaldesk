using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Clauses.Commands;

public record CreateClauseCommand(
    string Title,
    ClauseCategory Category,
    string Body,
    string Language,
    string? Guidance,
    string? RiskLevel,
    Guid? RequestTypeId) : ICommand<Guid>;

public class CreateClauseCommandValidator : AbstractValidator<CreateClauseCommand>
{
    public CreateClauseCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Body).NotEmpty();
        RuleFor(x => x.Guidance).MaximumLength(2000);
        RuleFor(x => x.Language).NotEmpty().Must(l => l is "en" or "fr")
            .WithMessage("Language must be 'en' or 'fr'.");
        RuleFor(x => x.RiskLevel).Must(r => r is null or "Low" or "Medium" or "High")
            .WithMessage("Risk level must be Low, Medium or High.");
    }
}

public class CreateClauseCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<CreateClauseCommandHandler> logger) : ICommandHandler<CreateClauseCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateClauseCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.EditClauses))
            throw new ForbiddenAccessException();

        var userId = currentUser.UserId!.Value;

        var clause = Clause.Create(
            command.Title,
            command.Category,
            command.Body,
            userId,
            command.Language,
            command.Guidance,
            command.RiskLevel,
            command.RequestTypeId);

        clause.SetCreatedBy(userId.ToString());
        db.Clauses.Add(clause);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Clause '{Title}' created by {UserId}", command.Title, userId);
        return clause.Id;
    }
}
