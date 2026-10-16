using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.RegulatoryWatch.Queries;

/// <summary>Regulatory watch listing for the people who curate it (administrators and managers).</summary>
public record GetRegulatoryTextsQuery(
    string? Search = null,
    RegulatoryCategory? Category = null,
    RegulatoryJurisdiction? Jurisdiction = null,
    string? Language = null,
    RegulatoryTextStatus? Status = null) : IQuery<List<RegulatoryTextDto>>;

public record RegulatoryTextDto(
    Guid Id,
    string Title,
    string? Reference,
    string Summary,
    string Keywords,
    string Language,
    RegulatoryCategory Category,
    RegulatoryJurisdiction Jurisdiction,
    RegulatoryTextStatus Status,
    DateTime? EffectiveDate,
    Guid? RequestTypeId,
    string? RequestTypeName,
    string? CreatedByName,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? SourceUrl = null,
    int ArticleCount = 0,
    DateTime? VerifiedAt = null,
    string? VerifiedByName = null);

public class GetRegulatoryTextsQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetRegulatoryTextsQuery, List<RegulatoryTextDto>>
{
    public async Task<List<RegulatoryTextDto>> HandleAsync(GetRegulatoryTextsQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator") && !currentUser.IsInRole("Manager"))
            throw new ForbiddenAccessException();

        var texts = db.RegulatoryTexts.AsNoTracking()
            .Include(r => r.RequestType)
            .Include(r => r.CreatedByUser)
            .AsQueryable();

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

        if (query.Status.HasValue)
            texts = texts.Where(r => r.Status == query.Status.Value);

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
