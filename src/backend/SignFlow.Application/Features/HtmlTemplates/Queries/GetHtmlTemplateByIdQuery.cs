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
    int FilledByStep,
    string? DefaultValue = null);

public record HtmlTemplateDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string HtmlContent,
    bool IsActive,
    Guid CreatedById,
    string CreatedByName,
    WorkflowType WorkflowType,
    bool HideRecipientIdentities,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<HtmlTemplateFieldDto> Fields,
    Guid? RequestTypeId,
    string? RequestTypeName,
    int? RequestTypeSlaHours,
    SignFlow.Application.Features.WorkflowPresets.TemplatePresetDto? WorkflowPreset = null,
    TemplateStatus Status = TemplateStatus.Approved,
    int Version = 1,
    Guid? ParentTemplateId = null,
    string? ApprovedByName = null,
    DateTime? ApprovedAt = null);

public class GetHtmlTemplateByIdQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetHtmlTemplateByIdQuery, HtmlTemplateDetailDto>
{
    public async Task<HtmlTemplateDetailDto> HandleAsync(GetHtmlTemplateByIdQuery query, CancellationToken cancellationToken = default)
    {
        var template = await db.HtmlTemplates
            .AsNoTracking()
            .Include(t => t.CreatedByUser)
            .Include(t => t.RequestType)
            .Include(t => t.Fields.OrderBy(f => f.Order))
            .FirstOrDefaultAsync(t => t.Id == query.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(HtmlTemplate), query.Id);

        // Non-admins can only view active templates (for creating requests)
        // A requester sees an approved, active version only (R-M6-5).
        if (!currentUser.Can(Permissions.ManageTemplates) && (!template.IsActive || template.Status != TemplateStatus.Approved))
            throw new ForbiddenAccessException();

        return new HtmlTemplateDetailDto(
            template.Id,
            template.Name,
            template.Description,
            template.HtmlContent,
            template.IsActive,
            template.CreatedById,
            $"{template.CreatedByUser.FirstName} {template.CreatedByUser.LastName}",
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
                f.FilledByStep,
                f.DefaultValue)).ToList(),
            template.RequestTypeId,
            template.RequestType?.Name,
            template.RequestType?.SlaHours,
            await SignFlow.Application.Features.WorkflowPresets.WorkflowPresetSummaries.LoadAsync(db, template.WorkflowPresetId, cancellationToken),
            template.Status, template.Version, template.ParentTemplateId,
            await SignFlow.Application.Features.PdfTemplates.Queries.GetPdfTemplateByIdQueryHandler.ApproverNameAsync(db, template.ApprovedById, cancellationToken),
            template.ApprovedAt);
    }
}
