using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Profile;

public record SessionDto(Guid Id, DateTime CreatedAt, DateTime LastUsedAt, DateTime ExpiresAt, string? IpAddress, string? UserAgent);

/// <summary>The current user's open sessions, most recently used first (R-M1-4).</summary>
public record GetMySessionsQuery : IQuery<List<SessionDto>>;

public class GetMySessionsQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IQueryHandler<GetMySessionsQuery, List<SessionDto>>
{
    public async Task<List<SessionDto>> HandleAsync(GetMySessionsQuery query, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();
        var now = DateTime.UtcNow;
        var idleCutoff = now - UserSession.IdleLifetime;
        return await db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now && s.LastUsedAt > idleCutoff)
            .OrderByDescending(s => s.LastUsedAt)
            .Select(s => new SessionDto(s.Id, s.CreatedAt, s.LastUsedAt, s.ExpiresAt, s.IpAddress, s.UserAgent))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>Ends one of the current user's sessions (a lost laptop, a shared computer).</summary>
public record EndMySessionCommand(Guid SessionId) : ICommand;

public class EndMySessionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : ICommandHandler<EndMySessionCommand>
{
    public async Task HandleAsync(EndMySessionCommand command, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == command.SessionId && s.UserId == userId, cancellationToken)
                      ?? throw new NotFoundException(nameof(UserSession), command.SessionId);
        session.Revoke("ended-by-user", DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }
}
