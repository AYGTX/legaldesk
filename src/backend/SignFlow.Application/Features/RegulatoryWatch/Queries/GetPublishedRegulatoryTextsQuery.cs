using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.RegulatoryWatch.Queries;

/// <summary>
/// Published regulatory texts, readable by any authenticated user and consumed internally by
/// compliance analysis. Unlike <see cref="GetRegulatoryTextsQuery"/> this never returns drafts
/// or archived texts.
/// </summary>
public record GetPublishedRegulatoryTextsQuery(
    string? Search = null,
    RegulatoryCategory? Category = null,
    RegulatoryJurisdiction? Jurisdiction = null,
    string? Language = null,
    Guid? RequestTypeId = null) : IQuery<List<RegulatoryTextDto>>;

public class GetPublishedRegulatoryTextsQueryHandler(
    IApplicationDbContext db) : IQueryHandler<GetPublishedRegulatoryTextsQuery, List<RegulatoryTextDto>>
{
    public async Task<List<RegulatoryTextDto>> HandleAsync(GetPublishedRegulatoryTextsQuery query, CancellationToken cancellationToken = default)
    {
        var texts = db.RegulatoryTexts.AsNoTracking()
            .Include(r => r.RequestType)
            .Include(r => r.CreatedByUser)
            .Where(r => r.Status == RegulatoryTextStatus.Published);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            texts = texts.Where(r => EF.Functions.Like(r.Title, $"%{term}%") || EF.Functions.Like(r.Summary, $"%{term}%"));
        }

        if (query.Category.HasValue)
            texts = texts.Where(r => r.Category == query.Category.Value);

        if (query.Jurisdiction.HasValue)
            texts = texts.Where(r => r.Jurisdiction == query.Jurisdiction.Value);

        if (!string.IsNullOrWhiteSpace(query.Language))
            texts = texts.Where(r => r.Language == query.Language);

        // A text scoped to a contract family stays available to every family unless the caller
        // narrows the list; an unscoped text always applies.
        if (query.RequestTypeId.HasValue)
            texts = texts.Where(r => r.RequestTypeId == null || r.RequestTypeId == query.RequestTypeId.Value);

        return await texts
            .OrderBy(r => r.Category).ThenBy(r => r.Title)
            .Select(r => new RegulatoryTextDto(
                r.Id, r.Title, r.Reference, r.Summary, r.Keywords, r.Language, r.Category, r.Jurisdiction,
                r.Status, r.EffectiveDate, r.RequestTypeId,
                r.RequestType != null ? r.RequestType.Name : null,
                r.CreatedByUser != null ? r.CreatedByUser.FullName : null,
                r.CreatedAt, r.UpdatedAt,
                r.SourceUrl,
                r.Articles.Count(),
                r.VerifiedAt,
                db.Users.Where(u => u.Id == r.VerifiedById).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }
}
