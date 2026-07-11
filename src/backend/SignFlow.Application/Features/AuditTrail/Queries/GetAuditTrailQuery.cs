using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.AuditTrail.Queries;

public record GetAuditTrailQuery(Guid DocumentId, int PageNumber = 1, int PageSize = 50) : IQuery<PagedResult<AuditLogDto>>;

public record AuditLogDto(Guid Id, AuditAction Action, string? Description, string IpAddress, string? UserAgent, string? Location, string? ActorName, string? ActorEmail, DateTime Timestamp, string? Metadata);

public class GetAuditTrailQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetAuditTrailQuery, PagedResult<AuditLogDto>>
{
    public async Task<PagedResult<AuditLogDto>> HandleAsync(GetAuditTrailQuery query, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == query.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), query.DocumentId);

        if (document.OwnerId != currentUser.UserId && !currentUser.IsInRole("Administrator"))
            throw new ForbiddenAccessException();

        var q = db.AuditLogs.AsNoTracking()
            .Where(a => a.DocumentId == query.DocumentId)
            .OrderByDescending(a => a.Timestamp);

        var totalCount = await q.CountAsync(cancellationToken);

        var items = await q
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(a => new AuditLogDto(a.Id, a.Action, a.Description, a.IpAddress, a.UserAgent, a.Location, a.ActorName, a.ActorEmail, a.Timestamp, a.Metadata))
            .ToListAsync(cancellationToken);

        return PagedResult<AuditLogDto>.Create(items, totalCount, query.PageNumber, query.PageSize);
    }
}
