using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.RequestTypes.Commands;

public record CreateRequestTypeCommand(
    string Name,
    string? Description,
    int SlaHours,
    string? Icon,
    string? Color) : ICommand<Guid>;

public class CreateRequestTypeCommandValidator : AbstractValidator<CreateRequestTypeCommand>
{
    public CreateRequestTypeCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.SlaHours).GreaterThan(0).WithMessage("SLA must be at least 1 hour.")
            .LessThanOrEqualTo(8760).WithMessage("SLA cannot exceed 1 year (8760 hours).");
        RuleFor(x => x.Icon).MaximumLength(60);
        RuleFor(x => x.Color).MaximumLength(20);
    }
}

public class CreateRequestTypeCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<CreateRequestTypeCommandHandler> logger) : ICommandHandler<CreateRequestTypeCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateRequestTypeCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator"))
            throw new ForbiddenAccessException();

        var nameExists = await db.RequestTypes.AnyAsync(t => t.Name == command.Name, cancellationToken);
        if (nameExists)
            throw new ConflictException($"A request type named '{command.Name}' already exists.");

        var type = RequestType.Create(command.Name, command.Description, command.SlaHours, command.Icon, command.Color);
        type.SetCreatedBy(currentUser.UserId!.Value.ToString());
        db.RequestTypes.Add(type);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Request type '{Name}' created by {UserId}", command.Name, currentUser.UserId);
        return type.Id;
    }
}
