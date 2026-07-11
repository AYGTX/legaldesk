using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Administration.Commands;

public record DeleteUserCommand(Guid UserId) : ICommand;

public class DeleteUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ILogger<DeleteUserCommandHandler> logger) : ICommandHandler<DeleteUserCommand>
{
    public async Task HandleAsync(DeleteUserCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator"))
            throw new ForbiddenAccessException();

        if (command.UserId == currentUser.UserId)
            throw new ConflictException("You cannot delete your own account.");

        var user = await userManager.FindByIdAsync(command.UserId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), command.UserId);

        // Refuse deletion when the user still owns documents (FK is Restrict) — deactivate instead.
        var ownsDocuments = await db.Documents.AnyAsync(d => d.OwnerId == command.UserId, cancellationToken);
        if (ownsDocuments)
            throw new ConflictException("This user owns documents and cannot be deleted. Deactivate the account instead.");

        // Guard against removing the last administrator.
        if (await userManager.IsInRoleAsync(user, "Administrator"))
        {
            var admins = await userManager.GetUsersInRoleAsync("Administrator");
            if (admins.Count <= 1)
                throw new ConflictException("Cannot delete the last administrator.");
        }

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
            throw new Common.Exceptions.ValidationException(result.Errors.Select(e => new FluentValidation.Results.ValidationFailure("", e.Description)));

        logger.LogInformation("Administrator {AdminId} deleted user {UserId}", currentUser.UserId, command.UserId);
    }
}
