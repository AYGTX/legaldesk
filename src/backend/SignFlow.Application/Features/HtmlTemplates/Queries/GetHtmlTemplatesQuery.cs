using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.HtmlTemplates.Queries;

public record GetHtmlTemplatesQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    bool? ActiveOnly = null) : IQuery<PagedResult<HtmlTemplateDto>>;

public record HtmlTemplateDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    Guid CreatedById,
    string CreatedByName,
    Guid? DefaultApproverId,
    string? DefaultApproverName,
    WorkflowType WorkflowType,
    int FieldCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public class GetHtmlTemplatesQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetHtmlTemplatesQuery, PagedResult<HtmlTemplateDto>>
{
    public async Task<PagedResult<HtmlTemplateDto>> HandleAsync(GetHtmlTemplatesQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator") && !currentUser.IsInRole("Manager"))
            throw new ForbiddenAccessException();

        var q = db.HtmlTemplates
            .AsNoTracking()
            .Include(t => t.CreatedByUser)
            .Include(t => t.DefaultApprover)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            q = q.Where(t => t.Name.ToLower().Contains(s) || (t.Description != null && t.Description.ToLower().Contains(s)));
        }

        if (query.ActiveOnly == true)
            q = q.Where(t => t.IsActive);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(t => t.CreatedAt)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(t => new HtmlTemplateDto(
                t.Id,
                t.Name,
                t.Description,
                t.IsActive,
                t.CreatedById,
                t.CreatedByUser.FirstName + " " + t.CreatedByUser.LastName,
                t.DefaultApproverId,
                t.DefaultApprover != null ? t.DefaultApprover.FirstName + " " + t.DefaultApprover.LastName : null,
                t.WorkflowType,
                t.Fields.Count,
                t.CreatedAt,
                t.UpdatedAt))
            .ToListAsync(cancellationToken);

        return PagedResult<HtmlTemplateDto>.Create(items, total, query.PageNumber, query.PageSize);
    }
}
