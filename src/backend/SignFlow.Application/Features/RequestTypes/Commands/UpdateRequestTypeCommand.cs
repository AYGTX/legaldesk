using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.RequestTypes.Commands;

public record UpdateRequestTypeCommand(
    Guid Id,
    string Name,
    string? Description,
    int SlaHours,
    string? Icon,
    string? Color,
    Guid? DefaultPresetId = null,
    List<string>? RuleSets = null) : ICommand;

public class UpdateRequestTypeCommandValidator : AbstractValidator<UpdateRequestTypeCommand>
{
    public UpdateRequestTypeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.SlaHours).GreaterThan(0).LessThanOrEqualTo(8760);
        RuleFor(x => x.Icon).MaximumLength(60);
        RuleFor(x => x.Color).MaximumLength(20);
    }
}

public class UpdateRequestTypeCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<UpdateRequestTypeCommandHandler> logger) : ICommandHandler<UpdateRequestTypeCommand>
{
    public async Task HandleAsync(UpdateRequestTypeCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageRequestTypes))
            throw new ForbiddenAccessException();

        var type = await db.RequestTypes.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(RequestType), command.Id);

        var nameTaken = await db.RequestTypes.AnyAsync(t => t.Name == command.Name && t.Id != command.Id, cancellationToken);
        if (nameTaken)
            throw new ConflictException($"A request type named '{command.Name}' already exists.");

        // Note: changing the SLA here only affects FUTURE requests. Existing documents keep the
        // ExpiresAt/SlaHours snapshot taken at their creation time.
        type.Update(command.Name, command.Description, command.SlaHours, command.Icon, command.Color);
        if (command.DefaultPresetId != type.DefaultPresetId) // a family may keep a workflow deactivated since
            await RequestTypeFamily.CheckPresetAsync(db, command.DefaultPresetId, cancellationToken);
        type.SetFamily(command.DefaultPresetId, command.RuleSets);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Request type {Id} updated by {UserId}", command.Id, currentUser.UserId);
    }
}
