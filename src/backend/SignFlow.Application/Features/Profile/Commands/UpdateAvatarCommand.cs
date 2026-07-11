using Microsoft.AspNetCore.Identity;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Profile.Commands;

public record UpdateAvatarCommand(Stream FileStream, string FileName, string ContentType) : ICommand<string>;

public class UpdateAvatarCommandHandler(
    UserManager<ApplicationUser> userManager,
    IStorageService storage,
    ICurrentUserService currentUser) : ICommandHandler<UpdateAvatarCommand, string>
{
    private static readonly string[] AllowedContentTypes = ["image/png", "image/jpeg", "image/jpg", "image/webp", "image/gif"];

    public async Task<string> HandleAsync(UpdateAvatarCommand command, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();

        if (!AllowedContentTypes.Contains(command.ContentType))
            throw new Common.Exceptions.ValidationException("Avatar must be a PNG, JPEG, WEBP, or GIF image.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        var avatarUrl = await storage.UploadAsync(command.FileStream, command.FileName, command.ContentType, $"avatars/{userId}", cancellationToken);

        user.AvatarUrl = avatarUrl;
        await userManager.UpdateAsync(user);

        return avatarUrl;
    }
}
