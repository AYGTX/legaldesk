using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Common.Security;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Auth.Commands;

public record InvitationDto(Guid Id, string Email, string FirstName, string LastName, string Role, DateTime CreatedAt,
    DateTime ExpiresAt, DateTime? AcceptedAt, DateTime? RevokedAt, string Status);

/// <summary>An Administrator invites someone into the organisation (R-M1-1); the e-mail carries a single-use link.</summary>
public record InviteUserCommand(string Email, string FirstName, string LastName, string Role) : ICommand<InvitationDto>;

public class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
    public static readonly string[] InvitableRoles = [Permissions.Administrator, Permissions.Manager, Permissions.Employee];

    public InviteUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).Must(r => InvitableRoles.Contains(r)).WithMessage("Choose Administrator, Manager or Employee.");
    }
}

public class InviteUserCommandHandler(
    IApplicationDbContext db,
    IPlatformQueries platform,
    ICurrentUserService currentUser,
    IEmailService email,
    IOptions<SecurityOptions> security,
    ILogger<InviteUserCommandHandler> logger) : ICommandHandler<InviteUserCommand, InvitationDto>
{
    public async Task<InvitationDto> HandleAsync(InviteUserCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageUsers))
            throw new ForbiddenAccessException();

        var address = command.Email.Trim();
        // E-mail addresses are unique across the whole install: a person signs in by e-mail alone.
        if (await platform.EmailInUseAsync(address, cancellationToken))
            throw new ConflictException("Someone already has an account with this e-mail.");

        var now = DateTime.UtcNow;
        // A new invitation to the same address replaces any open one.
        var open = await db.UserInvitations
            .Where(i => i.Email == address && i.AcceptedAt == null && i.RevokedAt == null)
            .ToListAsync(cancellationToken);
        open.ForEach(i => i.Revoke(now));

        var token = SecureTokens.Generate();
        var invitation = UserInvitation.Create(address, command.FirstName, command.LastName, command.Role,
            SecureTokens.Hash(token), currentUser.UserId!.Value, now);
        db.UserInvitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);

        var link = $"{security.Value.PublicUrl.TrimEnd('/')}/auth/accept-invitation?token={Uri.EscapeDataString(token)}";
        await email.SendTemplateAsync(address, "Invitation",
            new { invitation.FirstName, InvitedBy = currentUser.UserName, Link = link, invitation.ExpiresAt },
            "You are invited to LegalDesk", cancellationToken);

        logger.LogInformation("User {AdminId} invited {Email} as {Role}", currentUser.UserId, address, command.Role);
        return Invitations.ToDto(invitation, now);
    }
}

public record RevokeInvitationCommand(Guid InvitationId) : ICommand;

public class RevokeInvitationCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : ICommandHandler<RevokeInvitationCommand>
{
    public async Task HandleAsync(RevokeInvitationCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageUsers))
            throw new ForbiddenAccessException();
        var invitation = await db.UserInvitations.FirstOrDefaultAsync(i => i.Id == command.InvitationId, cancellationToken)
                         ?? throw new NotFoundException(nameof(UserInvitation), command.InvitationId);
        if (invitation.AcceptedAt is not null)
            throw new ConflictException("This invitation was already accepted.");
        invitation.Revoke(DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public record GetInvitationsQuery : IQuery<List<InvitationDto>>;

public class GetInvitationsQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IQueryHandler<GetInvitationsQuery, List<InvitationDto>>
{
    public async Task<List<InvitationDto>> HandleAsync(GetInvitationsQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageUsers))
            throw new ForbiddenAccessException();
        var now = DateTime.UtcNow;
        var rows = await db.UserInvitations.AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return rows.Select(i => Invitations.ToDto(i, now)).ToList();
    }
}

/// <summary>Public: the invited person sets a password; the account is created in the inviting organisation.</summary>
public record AcceptInvitationCommand(string Token, string Password, ClientInfo Client) : ICommand<AuthSession>;

public class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(256)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");
    }
}

public class AcceptInvitationCommandHandler(
    IApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ITenantContext tenant,
    AuthSessions sessions,
    ILogger<AcceptInvitationCommandHandler> logger) : ICommandHandler<AcceptInvitationCommand, AuthSession>
{
    public const string InvalidLink = "This invitation link is invalid or has expired. Ask your administrator for a new one.";

    public async Task<AuthSession> HandleAsync(AcceptInvitationCommand command, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var hash = SecureTokens.Hash(command.Token);
        // Anonymous request: no organisation in scope, so the lookup by token hash spans organisations.
        var invitation = await db.UserInvitations.FirstOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        if (invitation is null || !invitation.IsOpen(now))
            throw new NotFoundException(InvalidLink);

        using var scope = tenant.BeginScope(invitation.OrganisationId);

        var user = new ApplicationUser
        {
            UserName = invitation.Email,
            Email = invitation.Email,
            FirstName = invitation.FirstName,
            LastName = invitation.LastName,
            EmailConfirmed = true, // the link reached this mailbox
            IsActive = true
        };
        user.AssignOrganisation(invitation.OrganisationId);

        var created = await userManager.CreateAsync(user, command.Password);
        if (!created.Succeeded)
            throw new Common.Exceptions.ValidationException(created.Errors.Select(e => new FluentValidation.Results.ValidationFailure("password", e.Description)));
        await userManager.AddToRoleAsync(user, invitation.Role);

        invitation.Accept(user.Id, now);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Invitation {InvitationId} accepted by new user {UserId}", invitation.Id, user.Id);
        return await sessions.StartAsync(user, command.Client, cancellationToken);
    }
}

internal static class Invitations
{
    public static InvitationDto ToDto(UserInvitation i, DateTime now) => new(
        i.Id, i.Email, i.FirstName, i.LastName, i.Role, i.CreatedAt, i.ExpiresAt, i.AcceptedAt, i.RevokedAt,
        i.AcceptedAt is not null ? "Accepted" : i.RevokedAt is not null ? "Revoked" : now >= i.ExpiresAt ? "Expired" : "Pending");
}
