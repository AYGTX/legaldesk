using FluentValidation;
using Microsoft.AspNetCore.Identity;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Profile.Queries;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Profile.Commands;

public record UpdateProfileCommand(
    string FirstName,
    string LastName,
    string? Company,
    string? Department,
    string? Title) : ICommand<ProfileDto>;

public class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Company).MaximumLength(200);
        RuleFor(x => x.Department).MaximumLength(200);
        RuleFor(x => x.Title).MaximumLength(200);
    }
}

public class UpdateProfileCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUser) : ICommandHandler<UpdateProfileCommand, ProfileDto>
{
    public async Task<ProfileDto> HandleAsync(UpdateProfileCommand command, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        user.FirstName = command.FirstName;
        user.LastName = command.LastName;
        user.Company = command.Company;
        user.Department = command.Department;
        user.Title = command.Title;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new Common.Exceptions.ValidationException(result.Errors.Select(e => new FluentValidation.Results.ValidationFailure("", e.Description)));

        var roles = await userManager.GetRolesAsync(user);

        return new ProfileDto(
            user.Id, user.Email!, user.FirstName, user.LastName, user.FullName,
            user.Company, user.Department, user.Title, user.AvatarUrl, user.SignatureUrl, user.TwoFactorEnabled,
            user.CreatedAt, user.LastLoginAt, roles.ToList());
    }
}
