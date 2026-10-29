using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Clauses.Commands;

public record UpdateClauseCommand(
    Guid ClauseId,
    string Title,
    ClauseCategory Category,
    string Body,
    string Language,
    string? Guidance,
    string? RiskLevel,
    Guid? RequestTypeId) : ICommand;

public class UpdateClauseCommandValidator : AbstractValidator<UpdateClauseCommand>
{
    public UpdateClauseCommandValidator()
    {
        RuleFor(x => x.ClauseId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Body).NotEmpty();
        RuleFor(x => x.Guidance).MaximumLength(2000);
        RuleFor(x => x.Language).NotEmpty().Must(l => l is "en" or "fr")
            .WithMessage("Language must be 'en' or 'fr'.");
        RuleFor(x => x.RiskLevel).Must(r => r is null or "Low" or "Medium" or "High")
            .WithMessage("Risk level must be Low, Medium or High.");
    }
}

public class UpdateClauseCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<UpdateClauseCommandHandler> logger) : ICommandHandler<UpdateClauseCommand>
{
    public async Task HandleAsync(UpdateClauseCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.EditClauses))
            throw new ForbiddenAccessException();

        var clause = await db.Clauses.FirstOrDefaultAsync(c => c.Id == command.ClauseId, cancellationToken)
            ?? throw new NotFoundException(nameof(Clause), command.ClauseId);

        clause.Update(
            command.Title,
            command.Category,
            command.Body,
            command.Language,
            command.Guidance,
            command.RiskLevel,
            command.RequestTypeId);

        clause.SetUpdatedBy(currentUser.UserId!.Value.ToString());
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Clause {ClauseId} updated to version {Version} by {UserId}",
            clause.Id, clause.Version, currentUser.UserId);
    }
}
