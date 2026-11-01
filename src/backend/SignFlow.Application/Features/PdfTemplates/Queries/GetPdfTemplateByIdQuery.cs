using SignFlow.Application.Common.Security;
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
    int FilledByStep);

public record PdfTemplateDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int PageCount,
    string StoragePath,
    Guid CreatedById,
    string CreatedByName,
    WorkflowType WorkflowType,
    bool HideRecipientIdentities,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<PdfTemplateFieldDto> Fields,
    Guid? RequestTypeId,
    string? RequestTypeName,
    int? RequestTypeSlaHours,
    SignFlow.Application.Features.WorkflowPresets.TemplatePresetDto? WorkflowPreset = null,
    List<AnnexClauseDto>? AnnexClauses = null,
    TemplateStatus Status = TemplateStatus.Approved,
    int Version = 1,
    Guid? ParentTemplateId = null,
    string? ApprovedByName = null,
    DateTime? ApprovedAt = null);

/// <summary>A clause carried onto every request made from the template, as an annex.</summary>
public record AnnexClauseDto(Guid Id, string Title, int Version);

public class GetPdfTemplateByIdQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetPdfTemplateByIdQuery, PdfTemplateDetailDto>
{
    public async Task<PdfTemplateDetailDto> HandleAsync(GetPdfTemplateByIdQuery query, CancellationToken cancellationToken = default)
    {
        var template = await db.PdfTemplates
            .AsNoTracking()
            .Include(t => t.CreatedByUser)
            .Include(t => t.RequestType)
            .Include(t => t.Fields.OrderBy(f => f.Order))
            .FirstOrDefaultAsync(t => t.Id == query.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(PdfTemplate), query.Id);

        // Non-admins can only view active templates (for creating requests).
        // A requester sees an approved, active version only (R-M6-5).
        if (!currentUser.Can(Permissions.ManageTemplates) && (!template.IsActive || template.Status != TemplateStatus.Approved))
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
                f.FilledByStep)).ToList(),
            template.RequestTypeId,
            template.RequestType?.Name,
            template.RequestType?.SlaHours,
            await SignFlow.Application.Features.WorkflowPresets.WorkflowPresetSummaries.LoadAsync(db, template.WorkflowPresetId, cancellationToken),
            await LoadAnnexAsync(template.AnnexClauseIds, cancellationToken),
            template.Status, template.Version, template.ParentTemplateId,
            await ApproverNameAsync(db, template.ApprovedById, cancellationToken), template.ApprovedAt);
    }

    internal static async Task<string?> ApproverNameAsync(IApplicationDbContext db, Guid? id, CancellationToken ct) =>
        id is null ? null : await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct);

    private async Task<List<AnnexClauseDto>> LoadAnnexAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var found = await db.Clauses.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new AnnexClauseDto(c.Id, c.Title,
                c.Versions.Where(v => v.Status == ClauseStatus.Approved).Select(v => v.Number).FirstOrDefault())).ToListAsync(ct);
        return [.. ids.Select(id => found.FirstOrDefault(c => c.Id == id)).OfType<AnnexClauseDto>()];
    }
}
