using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Administration.Commands;

public record UpdateUserCommand(
    Guid UserId,
    string FirstName,
    string LastName,
    string? Company,
    string? Department,
    string? Title,
    bool IsActive,
    List<string> Roles) : ICommand;

public class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
    }
}

public class UpdateUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUser) : ICommandHandler<UpdateUserCommand>
{
    public async Task HandleAsync(UpdateUserCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageUsers))
            throw new ForbiddenAccessException();

        var user = await userManager.FindByIdAsync(command.UserId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), command.UserId);

        user.FirstName = command.FirstName;
        user.LastName = command.LastName;
        user.Company = command.Company;
        user.Department = command.Department;
        user.Title = command.Title;
        user.IsActive = command.IsActive;

        await userManager.UpdateAsync(user);

        var currentRoles = await userManager.GetRolesAsync(user);
        await userManager.RemoveFromRolesAsync(user, currentRoles);
        await userManager.AddToRolesAsync(user, command.Roles);
    }
}
