using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.RegulatoryWatch.Queries;

/// <summary>How pasted law text would be cut into articles — nothing is saved, so a person can check it first.</summary>
public record PreviewRegulatoryArticlesQuery(string RawText) : IQuery<ArticleSplit>;

public class PreviewRegulatoryArticlesQueryHandler(ICurrentUserService currentUser)
    : IQueryHandler<PreviewRegulatoryArticlesQuery, ArticleSplit>
{
    public Task<ArticleSplit> HandleAsync(PreviewRegulatoryArticlesQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator") && !currentUser.IsInRole("Manager"))
            throw new ForbiddenAccessException();

        return Task.FromResult(LegalArticleSplitter.Split(query.RawText));
    }
}

public record GetRegulatoryArticlesQuery(Guid RegulatoryTextId) : IQuery<List<RegulatoryArticleDto>>;

public record RegulatoryArticleDto(Guid Id, string Number, string Text, int Order);

public class GetRegulatoryArticlesQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetRegulatoryArticlesQuery, List<RegulatoryArticleDto>>
{
    public async Task<List<RegulatoryArticleDto>> HandleAsync(GetRegulatoryArticlesQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator") && !currentUser.IsInRole("Manager"))
            throw new ForbiddenAccessException();

        if (!await db.RegulatoryTexts.AnyAsync(r => r.Id == query.RegulatoryTextId, cancellationToken))
            throw new NotFoundException(nameof(RegulatoryText), query.RegulatoryTextId);

        return await db.RegulatoryArticles.AsNoTracking()
            .Where(a => a.RegulatoryTextId == query.RegulatoryTextId)
            .OrderBy(a => a.Order)
            .Select(a => new RegulatoryArticleDto(a.Id, a.Number, a.Text, a.Order))
            .ToListAsync(cancellationToken);
    }
}
