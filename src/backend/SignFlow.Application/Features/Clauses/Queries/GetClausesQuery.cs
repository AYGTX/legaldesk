using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Clauses.Queries;

/// <summary>Clause library listing for the people who curate it (administrators and managers).</summary>
public record GetClausesQuery(
    string? Search = null,
    ClauseCategory? Category = null,
    string? Language = null,
    ClauseStatus? Status = null) : IQuery<List<ClauseDto>>;

public record ClauseDto(
    Guid Id,
    string Title,
    ClauseCategory Category,
    string Body,
    string? Guidance,
    string Language,
    ClauseStatus Status,
    string? RiskLevel,
    int Version,
    int UsageCount,
    Guid? RequestTypeId,
    string? RequestTypeName,
    string? CreatedByName,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public class GetClausesQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetClausesQuery, List<ClauseDto>>
{
    public async Task<List<ClauseDto>> HandleAsync(GetClausesQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator") && !currentUser.IsInRole("Manager"))
            throw new ForbiddenAccessException();

        var clauses = db.Clauses.AsNoTracking()
            .Include(c => c.RequestType)
            .Include(c => c.CreatedByUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            clauses = clauses.Where(c => EF.Functions.Like(c.Title, $"%{term}%") || EF.Functions.Like(c.Body, $"%{term}%"));
        }

        if (query.Category.HasValue)
            clauses = clauses.Where(c => c.Category == query.Category.Value);

        if (!string.IsNullOrWhiteSpace(query.Language))
            clauses = clauses.Where(c => c.Language == query.Language);

        if (query.Status.HasValue)
            clauses = clauses.Where(c => c.Status == query.Status.Value);

        return await clauses
            .OrderBy(c => c.Category).ThenBy(c => c.Title)
            .Select(c => new ClauseDto(
                c.Id, c.Title, c.Category, c.Body, c.Guidance, c.Language, c.Status, c.RiskLevel,
                c.Version, c.UsageCount, c.RequestTypeId,
                c.RequestType != null ? c.RequestType.Name : null,
                c.CreatedByUser != null ? c.CreatedByUser.FullName : null,
                c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
