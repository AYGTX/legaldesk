using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Security;

namespace SignFlow.Application.Features.Comments.Queries;

/// <summary>The people a comment on this contract can mention (R-M7-1): the organisation's active members, and the
/// contract's participants. The composer offers only these; the server checks the same list again.</summary>
public record GetMentionablesQuery(Guid DocumentId) : IQuery<List<MentionableDto>>;

/// <param name="IsParticipant">A participant: never offered for an internal note.</param>
public record MentionableDto(Guid Id, string Name, string Email, bool IsParticipant);

public class GetMentionablesQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetMentionablesQuery, List<MentionableDto>>
{
    public async Task<List<MentionableDto>> HandleAsync(GetMentionablesQuery query, CancellationToken cancellationToken = default)
    {
        if (!await DocumentReadAccess.CanReadAsync(db, currentUser, query.DocumentId, cancellationToken))
            throw new ForbiddenAccessException();

        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Id != currentUser.UserId)
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .Take(500)
            .ToListAsync(cancellationToken);

        var participants = await db.Recipients.AsNoTracking()
            .Where(r => r.DocumentId == query.DocumentId && r.RetiredAt == null && r.Email != Domain.Entities.Recipient.PoolAddress)
            .OrderBy(r => r.Order)
            .Select(r => new { r.Id, r.Name, r.Email })
            .ToListAsync(cancellationToken);

        return
        [
            .. users.Select(u => new MentionableDto(u.Id, $"{u.FirstName} {u.LastName}".Trim(), u.Email ?? string.Empty, false)),
            .. participants
                .Where(p => !string.Equals(p.Email, currentUser.UserEmail, StringComparison.OrdinalIgnoreCase))
                .Select(p => new MentionableDto(p.Id, p.Name, p.Email, true))
        ];
    }
}
