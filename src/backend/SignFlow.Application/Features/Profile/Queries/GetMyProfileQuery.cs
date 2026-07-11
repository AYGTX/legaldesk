using Microsoft.AspNetCore.Identity;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Profile.Queries;

public record GetMyProfileQuery : IQuery<ProfileDto>;

public record ProfileDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? Company,
    string? Department,
    string? Title,
    string? AvatarUrl,
    string? SignatureUrl,
    bool MfaEnabled,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    List<string> Roles);

public class GetMyProfileQueryHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUser) : IQueryHandler<GetMyProfileQuery, ProfileDto>
{
    public async Task<ProfileDto> HandleAsync(GetMyProfileQuery query, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        var roles = await userManager.GetRolesAsync(user);

        return new ProfileDto(
            user.Id,
            user.Email!,
            user.FirstName,
            user.LastName,
            user.FullName,
            user.Company,
            user.Department,
            user.Title,
            user.AvatarUrl,
            user.SignatureUrl,
            user.MfaEnabled,
            user.CreatedAt,
            user.LastLoginAt,
            roles.ToList());
    }
}
