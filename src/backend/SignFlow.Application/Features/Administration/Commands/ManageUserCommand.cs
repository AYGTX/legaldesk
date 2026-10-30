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
    ICurrentUserService currentUser,
    SignFlow.Application.Features.Auth.AuthSessions sessions) : ICommandHandler<UpdateUserCommand>
{
    public async Task HandleAsync(UpdateUserCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageUsers))
            throw new ForbiddenAccessException();

        var user = await userManager.FindByIdAsync(command.UserId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), command.UserId);

        var roles = command.Roles.Distinct().ToList();
        if (roles.Count == 0 || roles.Any(r => !SignFlow.Application.Features.Auth.Commands.InviteUserCommandValidator.InvitableRoles.Contains(r)))
            throw new Common.Exceptions.ValidationException([new FluentValidation.Results.ValidationFailure("roles", "Choose Administrator, Manager or Employee.")]);

        var currentRoles = await userManager.GetRolesAsync(user);
        var losesAdmin = currentRoles.Contains(Permissions.Administrator)
                         && (!command.IsActive || !roles.Contains(Permissions.Administrator));
        if (losesAdmin)
        {
            if (user.Id == currentUser.UserId)
                throw new ConflictException("You cannot remove your own administrator rights or deactivate yourself.");
            var admins = (await userManager.GetUsersInRoleAsync(Permissions.Administrator)).Count(u => u.IsActive);
            if (admins <= 1)
                throw new ConflictException("The organisation must keep at least one active administrator.");
        }

        var deactivated = user.IsActive && !command.IsActive;
        user.FirstName = command.FirstName;
        user.LastName = command.LastName;
        user.Company = command.Company;
        user.Department = command.Department;
        user.Title = command.Title;
        user.IsActive = command.IsActive;

        await userManager.UpdateAsync(user);
        await userManager.RemoveFromRolesAsync(user, currentRoles);
        await userManager.AddToRolesAsync(user, roles);

        // A deactivated account is signed out everywhere at once, not when its access token runs out.
        if (deactivated)
        {
            await userManager.UpdateSecurityStampAsync(user);
            await sessions.EndAllAsync(user.Id, "deactivated", cancellationToken);
        }
    }
}
