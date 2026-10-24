using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Queries;

public record GetDocumentsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    DocumentStatus? Status = null,
    string? SortBy = "createdAt",
    bool Descending = true,
    Guid? RequestTypeId = null,
    IReadOnlyList<DocumentStatus>? Statuses = null) : IQuery<PagedResult<DocumentSummaryDto>>;

public record DocumentSummaryDto(
    Guid Id,
    string Title,
    DocumentStatus Status,
    string FileName,
    long FileSizeBytes,
    int PageCount,
    int RecipientCount,
    int SignedCount,
    DateTime CreatedAt,
    DateTime? SentAt,
    DateTime? CompletedAt,
    DateTime? ExpiresAt,
    string? OwnerName,
    string? OwnerEmail,
    string? Tags,
    string? RequestTypeName);

public class GetDocumentsQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetDocumentsQuery, PagedResult<DocumentSummaryDto>>
{
    public async Task<PagedResult<DocumentSummaryDto>> HandleAsync(GetDocumentsQuery query, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId!.Value;
        var isAdmin = currentUser.IsInRole("Administrator");

        var q = db.Documents.AsNoTracking()
            .Where(d => !d.IsDeleted && !d.IsTemplate)
            .Where(d => isAdmin || d.OwnerId == userId);

        if (query.Status.HasValue)
            q = q.Where(d => d.Status == query.Status.Value);

        if (query.Statuses is { Count: > 0 })
        {
            var statuses = query.Statuses;
            q = q.Where(d => statuses.Contains(d.Status));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            q = q.Where(d => d.Title.ToLower().Contains(s) || d.FileName.ToLower().Contains(s));
        }

        if (query.RequestTypeId.HasValue)
            q = q.Where(d => d.RequestTypeId == query.RequestTypeId.Value);

        var totalCount = await q.CountAsync(cancellationToken);

        q = query.SortBy?.ToLower() switch
        {
            "title" => query.Descending ? q.OrderByDescending(d => d.Title) : q.OrderBy(d => d.Title),
            "status" => query.Descending ? q.OrderByDescending(d => d.Status) : q.OrderBy(d => d.Status),
            "expiresat" => query.Descending ? q.OrderByDescending(d => d.ExpiresAt) : q.OrderBy(d => d.ExpiresAt),
            _ => query.Descending ? q.OrderByDescending(d => d.CreatedAt) : q.OrderBy(d => d.CreatedAt)
        };

        var items = await q
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(d => new DocumentSummaryDto(
                d.Id,
                d.Title,
                d.Status,
                d.FileName,
                d.FileSizeBytes,
                d.PageCount,
                d.Recipients.Count,
                d.Recipients.Count(r => r.Status == WorkflowStepStatus.Completed),
                d.CreatedAt,
                d.SentAt,
                d.CompletedAt,
                d.ExpiresAt,
                d.Owner.FullName,
                d.Owner.Email,
                d.Tags,
                d.RequestType != null ? d.RequestType.Name : null))
            .ToListAsync(cancellationToken);

        return PagedResult<DocumentSummaryDto>.Create(items, totalCount, query.PageNumber, query.PageSize);
    }
}
