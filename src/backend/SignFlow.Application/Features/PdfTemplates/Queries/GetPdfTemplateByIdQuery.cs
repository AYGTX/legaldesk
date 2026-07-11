using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.PdfTemplates.Queries;

public record GetPdfTemplateByIdQuery(Guid Id) : IQuery<PdfTemplateDetailDto>;

public record PdfTemplateFieldDto(
    Guid Id,
    string FieldKey,
    string Label,
    PdfFieldType Type,
    PdfFieldFillRole FillRole,
    int Page,
    float X,
    float Y,
    float Width,
    float Height,
    bool IsRequired,
    int Order,
    string? AutoFillFrom,
    string? DefaultValue,
    string? OptionsJson,
    Guid? PreAssignedApproverId,
    string? PreAssignedApproverName,
    int WorkflowStage);

public record PdfTemplateDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int PageCount,
    string StoragePath,
    Guid CreatedById,
    string CreatedByName,
    Guid? DefaultApproverId,
    string? DefaultApproverName,
    WorkflowType WorkflowType,
    bool HideRecipientIdentities,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<PdfTemplateFieldDto> Fields);

public class GetPdfTemplateByIdQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetPdfTemplateByIdQuery, PdfTemplateDetailDto>
{
    public async Task<PdfTemplateDetailDto> HandleAsync(GetPdfTemplateByIdQuery query, CancellationToken cancellationToken = default)
    {
        var template = await db.PdfTemplates
            .AsNoTracking()
            .Include(t => t.CreatedByUser)
            .Include(t => t.DefaultApprover)
            .Include(t => t.Fields.OrderBy(f => f.Order))
                .ThenInclude(f => f.PreAssignedApprover)
            .FirstOrDefaultAsync(t => t.Id == query.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(PdfTemplate), query.Id);

        // Non-admins can only view active templates (for creating requests).
        if (!currentUser.IsInRole("Administrator") && !currentUser.IsInRole("Manager") && !template.IsActive)
            throw new ForbiddenAccessException();

        return new PdfTemplateDetailDto(
            template.Id,
            template.Name,
            template.Description,
            template.IsActive,
            template.PageCount,
            template.StoragePath,
            template.CreatedById,
            $"{template.CreatedByUser.FirstName} {template.CreatedByUser.LastName}",
            template.DefaultApproverId,
            template.DefaultApprover is not null ? $"{template.DefaultApprover.FirstName} {template.DefaultApprover.LastName}" : null,
            template.WorkflowType,
            template.HideRecipientIdentities,
            template.CreatedAt,
            template.UpdatedAt,
            template.Fields.Select(f => new PdfTemplateFieldDto(
                f.Id,
                f.FieldKey,
                f.Label,
                f.Type,
                f.FillRole,
                f.Page,
                f.X, f.Y, f.Width, f.Height,
                f.IsRequired,
                f.Order,
                f.AutoFillFrom,
                f.DefaultValue,
                f.OptionsJson,
                f.PreAssignedApproverId,
                f.PreAssignedApprover is not null ? $"{f.PreAssignedApprover.FirstName} {f.PreAssignedApprover.LastName}" : null,
                f.WorkflowStage)).ToList());
    }
}
