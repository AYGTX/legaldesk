using SignFlow.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.HtmlTemplates.Queries;

public record GetHtmlTemplateByIdQuery(Guid Id) : IQuery<HtmlTemplateDetailDto>;

public record HtmlTemplateFieldDto(
    Guid Id,
    string FieldKey,
    string Label,
    HtmlTemplateFieldType Type,
    HtmlTemplateFieldRole Role,
    bool IsRequired,
    int Order,
    string? AutoFillFrom,
    string? OptionsJson,
    Guid? PreAssignedApproverId,
    string? PreAssignedApproverName,
    int WorkflowStage,
    string? DefaultValue = null);

public record HtmlTemplateDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string HtmlContent,
    bool IsActive,
    Guid CreatedById,
    string CreatedByName,
    Guid? DefaultApproverId,
    string? DefaultApproverName,
    WorkflowType WorkflowType,
    bool HideRecipientIdentities,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<HtmlTemplateFieldDto> Fields,
    Guid? RequestTypeId,
    string? RequestTypeName,
    int? RequestTypeSlaHours,
    SignFlow.Application.Features.WorkflowPresets.TemplatePresetDto? WorkflowPreset = null);

public class GetHtmlTemplateByIdQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetHtmlTemplateByIdQuery, HtmlTemplateDetailDto>
{
    public async Task<HtmlTemplateDetailDto> HandleAsync(GetHtmlTemplateByIdQuery query, CancellationToken cancellationToken = default)
    {
        var template = await db.HtmlTemplates
            .AsNoTracking()
            .Include(t => t.CreatedByUser)
            .Include(t => t.DefaultApprover)
            .Include(t => t.RequestType)
            .Include(t => t.Fields.OrderBy(f => f.Order))
                .ThenInclude(f => f.PreAssignedApprover)
            .FirstOrDefaultAsync(t => t.Id == query.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(HtmlTemplate), query.Id);

        // Non-admins can only view active templates (for creating requests)
        if (!currentUser.Can(Permissions.ManageTemplates) && !template.IsActive)
            throw new ForbiddenAccessException();

        return new HtmlTemplateDetailDto(
            template.Id,
            template.Name,
            template.Description,
            template.HtmlContent,
            template.IsActive,
            template.CreatedById,
            $"{template.CreatedByUser.FirstName} {template.CreatedByUser.LastName}",
            template.DefaultApproverId,
            template.DefaultApprover is not null ? $"{template.DefaultApprover.FirstName} {template.DefaultApprover.LastName}" : null,
            template.WorkflowType,
            template.HideRecipientIdentities,
            template.CreatedAt,
            template.UpdatedAt,
            template.Fields.Select(f => new HtmlTemplateFieldDto(
                f.Id,
                f.FieldKey,
                f.Label,
                f.Type,
                f.Role,
                f.IsRequired,
                f.Order,
                f.AutoFillFrom,
                f.OptionsJson,
                f.PreAssignedApproverId,
                f.PreAssignedApprover is not null ? $"{f.PreAssignedApprover.FirstName} {f.PreAssignedApprover.LastName}" : null,
                f.WorkflowStage,
                f.DefaultValue)).ToList(),
            template.RequestTypeId,
            template.RequestType?.Name,
            template.RequestType?.SlaHours,
            await SignFlow.Application.Features.WorkflowPresets.WorkflowPresetSummaries.LoadAsync(db, template.WorkflowPresetId, cancellationToken));
    }
}
