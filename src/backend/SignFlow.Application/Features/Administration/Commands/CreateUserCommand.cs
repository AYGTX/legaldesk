using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Administration.Commands;

public record CreateUserCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? Company,
    string? Department,
    string? Title,
    List<string> Roles) : ICommand<Guid>;

public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");
        RuleFor(x => x.Roles).NotEmpty().WithMessage("At least one role is required.");
    }
}

public class CreateUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    ICurrentUserService currentUser,
    ILogger<CreateUserCommandHandler> logger) : ICommandHandler<CreateUserCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator"))
            throw new ForbiddenAccessException();

        var existing = await userManager.FindByEmailAsync(command.Email);
        if (existing is not null)
            throw new ConflictException($"An account with email '{command.Email}' already exists.");

        foreach (var role in command.Roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                throw new Common.Exceptions.ValidationException($"Role '{role}' does not exist.");
        }

        var user = new ApplicationUser
        {
            UserName = command.Email,
            Email = command.Email,
            FirstName = command.FirstName,
            LastName = command.LastName,
            Company = command.Company,
            Department = command.Department,
            Title = command.Title,
            EmailConfirmed = true,
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, command.Password);
        if (!result.Succeeded)
            throw new Common.Exceptions.ValidationException(result.Errors.Select(e => new FluentValidation.Results.ValidationFailure("", e.Description)));

        await userManager.AddToRolesAsync(user, command.Roles);

        logger.LogInformation("Administrator {AdminId} created user {Email}", currentUser.UserId, command.Email);

        return user.Id;
    }
}
