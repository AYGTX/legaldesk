using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Clauses.Queries;

/// <summary>
/// Approved clauses offered to a drafter composing a contract template. Unlike
/// <see cref="GetClausesQuery"/> this is readable by any authenticated user and never returns
/// draft or deprecated wording.
/// </summary>
public record GetApprovedClausesQuery(
    string? Search = null,
    ClauseCategory? Category = null,
    string? Language = null,
    Guid? RequestTypeId = null) : IQuery<List<ClauseDto>>;

public class GetApprovedClausesQueryHandler(
    IApplicationDbContext db) : IQueryHandler<GetApprovedClausesQuery, List<ClauseDto>>
{
    public async Task<List<ClauseDto>> HandleAsync(GetApprovedClausesQuery query, CancellationToken cancellationToken = default)
    {
        var clauses = db.Clauses.AsNoTracking()
            .Include(c => c.RequestType)
            .Include(c => c.CreatedByUser)
            .Where(c => c.Status == ClauseStatus.Approved);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            clauses = clauses.Where(c => EF.Functions.Like(c.Title, $"%{term}%") || EF.Functions.Like(c.Body, $"%{term}%"));
        }

        if (query.Category.HasValue)
            clauses = clauses.Where(c => c.Category == query.Category.Value);

        if (!string.IsNullOrWhiteSpace(query.Language))
            clauses = clauses.Where(c => c.Language == query.Language);

        // A clause scoped to a contract family stays available to every family unless the caller
        // narrows the list; an unscoped clause is always offered.
        if (query.RequestTypeId.HasValue)
            clauses = clauses.Where(c => c.RequestTypeId == null || c.RequestTypeId == query.RequestTypeId.Value);

        return await clauses
            .OrderByDescending(c => c.UsageCount).ThenBy(c => c.Title)
            .Select(c => new ClauseDto(
                c.Id, c.Title, c.Category, c.Body, c.Guidance, c.Language, c.Status, c.RiskLevel,
                c.Version, c.UsageCount, c.RequestTypeId,
                c.RequestType != null ? c.RequestType.Name : null,
                c.CreatedByUser != null ? c.CreatedByUser.FullName : null,
                c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
