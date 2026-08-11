using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.AuditTrail.Queries;

public record GetOrgAuditLogQuery(int PageNumber = 1, int PageSize = 50, string? Search = null, AuditAction? Action = null) : IQuery<PagedResult<OrgAuditLogDto>>;

public record OrgAuditLogDto(Guid Id, Guid DocumentId, string DocumentTitle, AuditAction Action, string? Description, string IpAddress, string? ActorName, string? ActorEmail, DateTime Timestamp);

public class GetOrgAuditLogQueryHandler(IApplicationDbContext db) : IQueryHandler<GetOrgAuditLogQuery, PagedResult<OrgAuditLogDto>>
{
    public async Task<PagedResult<OrgAuditLogDto>> HandleAsync(GetOrgAuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var q =
            from a in db.AuditLogs.AsNoTracking()
            join d in db.Documents.AsNoTracking() on a.DocumentId equals d.Id
            where !d.IsDeleted
            select new { a, d.Title };

        if (query.Action.HasValue)
            q = q.Where(x => x.a.Action == query.Action.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            q = q.Where(x =>
                x.Title.ToLower().Contains(s) ||
                (x.a.ActorName != null && x.a.ActorName.ToLower().Contains(s)) ||
                (x.a.ActorEmail != null && x.a.ActorEmail.ToLower().Contains(s)));
        }

        q = q.OrderByDescending(x => x.a.Timestamp);

        var totalCount = await q.CountAsync(cancellationToken);

        var items = await q
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new OrgAuditLogDto(x.a.Id, x.a.DocumentId, x.Title, x.a.Action, x.a.Description, x.a.IpAddress, x.a.ActorName, x.a.ActorEmail, x.a.Timestamp))
            .ToListAsync(cancellationToken);

        return PagedResult<OrgAuditLogDto>.Create(items, totalCount, query.PageNumber, query.PageSize);
    }
}
