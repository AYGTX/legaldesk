using SignFlow.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.PdfTemplates.Queries;

public record GetPdfTemplatesQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    bool? ActiveOnly = null) : IQuery<PagedResult<PdfTemplateDto>>;

public record PdfTemplateDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int PageCount,
    Guid CreatedById,
    string CreatedByName,
    Guid? DefaultApproverId,
    string? DefaultApproverName,
    WorkflowType WorkflowType,
    int FieldCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public class GetPdfTemplatesQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetPdfTemplatesQuery, PagedResult<PdfTemplateDto>>
{
    public async Task<PagedResult<PdfTemplateDto>> HandleAsync(GetPdfTemplatesQuery query, CancellationToken cancellationToken = default)
    {
        // Admins/managers manage templates; regular users list active templates to create requests from.
        var adminView = currentUser.Can(Permissions.ManageTemplates);

        var q = db.PdfTemplates
            .AsNoTracking()
            .Include(t => t.CreatedByUser)
            .Include(t => t.DefaultApprover)
            .AsQueryable();

        if (!adminView || query.ActiveOnly == true)
            q = q.Where(t => t.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            q = q.Where(t => t.Name.ToLower().Contains(s) || (t.Description != null && t.Description.ToLower().Contains(s)));
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(t => t.CreatedAt)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(t => new PdfTemplateDto(
                t.Id,
                t.Name,
                t.Description,
                t.IsActive,
                t.PageCount,
                t.CreatedById,
                t.CreatedByUser.FirstName + " " + t.CreatedByUser.LastName,
                t.DefaultApproverId,
                t.DefaultApprover != null ? t.DefaultApprover.FirstName + " " + t.DefaultApprover.LastName : null,
                t.WorkflowType,
                t.Fields.Count,
                t.CreatedAt,
                t.UpdatedAt))
            .ToListAsync(cancellationToken);

        return PagedResult<PdfTemplateDto>.Create(items, total, query.PageNumber, query.PageSize);
    }
}
