using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Queries;

public record GetTemplateRevisionFormQuery(Guid DocumentId) : IQuery<TemplateRevisionFormDto>;

public record TemplateRevisionFieldDto(string FieldKey, string Label, string Type, bool IsRequired, string? OptionsJson);

/// <summary>What a reviewer asked to change, so the owner revises against the actual request.</summary>
public record ChangeRequestDto(string RequestedBy, string? Reason);

/// <summary>
/// The form an owner fills again after a reviewer asked for changes on a template contract: only the answers the
/// owner may change, with their current values.
/// </summary>
public record TemplateRevisionFormDto(
    string TemplateKind,
    int CurrentVersion,
    List<TemplateRevisionFieldDto> Fields,
    Dictionary<string, string> Values,
    List<ChangeRequestDto> ChangeRequests);

public class GetTemplateRevisionFormQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : IQueryHandler<GetTemplateRevisionFormQuery, TemplateRevisionFormDto>
{
    public async Task<TemplateRevisionFormDto> HandleAsync(GetTemplateRevisionFormQuery query, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.AsNoTracking()
            .Include(d => d.Recipients)
            .FirstOrDefaultAsync(d => d.Id == query.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), query.DocumentId);

        if (document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        var kind = TemplateRevisionSupport.KindOf(document)
            ?? throw new ConflictException("Only contracts built from a template are revised by changing their answers.");

        if (document.Status != DocumentStatus.ChangesRequested)
            throw new ConflictException("Answers can only be revised after a participant requested changes.");

        var fields = await TemplateRevisionSupport.LoadFieldsAsync(db, document, cancellationToken)
            ?? throw new ConflictException("The template this contract was built from no longer exists, so its answers cannot be revised.");

        var requests = document.Recipients
            .Where(r => r.Status == WorkflowStepStatus.ChangesRequested)
            .Select(r => new ChangeRequestDto(document.HideRecipientIdentities ? string.Empty : r.Name, r.DeclineReason))
            .ToList();

        return new TemplateRevisionFormDto(
            kind,
            document.Version,
            fields.Where(f => f.Editable)
                .Select(f => new TemplateRevisionFieldDto(f.Key, f.Label, f.Type, f.IsRequired, f.OptionsJson))
                .ToList(),
            TemplateRevisionSupport.ParseValues(document.FilledValuesJson),
            requests);
    }
}
