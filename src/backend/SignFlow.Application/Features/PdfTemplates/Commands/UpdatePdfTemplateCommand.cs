using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.PdfTemplates.Commands;

/// <summary>
/// Updates a PDF template's metadata/workflow config and replaces its full field set (the coordinate
/// designer's save). The stored base PDF is immutable and untouched here.
/// </summary>
public record UpdatePdfTemplateCommand(
    Guid Id,
    string Name,
    string? Description,
    Guid? DefaultApproverId,
    bool IsActive,
    List<PdfTemplateFieldInput> Fields,
    WorkflowType WorkflowType = WorkflowType.Sequential,
    bool HideRecipientIdentities = false,
    Guid? RequestTypeId = null,
    Guid? WorkflowPresetId = null,
    /// <summary>Clauses always appended, as an annex, to requests made from this template.</summary>
    List<Guid>? AnnexClauseIds = null) : ICommand;

public class UpdatePdfTemplateCommandValidator : AbstractValidator<UpdatePdfTemplateCommand>
{
    public UpdatePdfTemplateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleForEach(x => x.Fields).ChildRules(f =>
        {
            f.RuleFor(x => x.FieldKey).NotEmpty().MaximumLength(100);
            f.RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
            f.RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        });
        RuleFor(x => x.Fields)
            .Must(fields => fields.Select(f => f.FieldKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() == fields.Count)
            .WithMessage("Field keys must be unique within a template.");
    }
}

public class UpdatePdfTemplateCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<UpdatePdfTemplateCommand>
{
    public async Task HandleAsync(UpdatePdfTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageTemplates))
            throw new ForbiddenAccessException();

        var template = await db.PdfTemplates
            .Include(t => t.Fields)
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(PdfTemplate), command.Id);

        var approverIds = command.Fields
            .Where(f => f.PreAssignedApproverId.HasValue)
            .Select(f => f.PreAssignedApproverId!.Value)
            .Distinct()
            .ToList();
        if (command.DefaultApproverId.HasValue)
            approverIds.Add(command.DefaultApproverId.Value);

        if (approverIds.Count > 0)
        {
            var existingCount = await db.Users.CountAsync(u => approverIds.Contains(u.Id), cancellationToken);
            if (existingCount != approverIds.Distinct().Count())
                throw new NotFoundException(nameof(ApplicationUser), approverIds.First());
        }

        if (command.RequestTypeId.HasValue)
        {
            var requestTypeExists = await db.RequestTypes
                .AnyAsync(t => t.Id == command.RequestTypeId.Value && t.IsActive, cancellationToken);
            if (!requestTypeExists)
                throw new Common.Exceptions.ValidationException("The selected request type is invalid or inactive.");
        }

        if (command.WorkflowPresetId.HasValue
            && command.WorkflowPresetId != template.WorkflowPresetId // a template may keep a preset that was deactivated since
            && !await db.WorkflowPresets.AnyAsync(p => p.Id == command.WorkflowPresetId.Value && p.IsActive, cancellationToken))
            throw new Common.Exceptions.ValidationException("The selected workflow is invalid or inactive.");

        template.Update(
            command.Name,
            command.Description,
            command.DefaultApproverId,
            command.IsActive,
            command.WorkflowType,
            command.HideRecipientIdentities,
            command.RequestTypeId);
        template.SetWorkflowPreset(command.WorkflowPresetId);

        var annexIds = (command.AnnexClauseIds ?? []).Distinct().ToList();
        if (annexIds.Count > 0)
        {
            // A clause already on the template may have been deprecated since; it stays until someone removes it.
            var allowed = await db.Clauses.CountAsync(c => annexIds.Contains(c.Id)
                && (c.Versions.Any(v => v.Status == ClauseStatus.Approved) || template.AnnexClauseIds.Contains(c.Id)), cancellationToken);
            if (allowed != annexIds.Count)
                throw new Common.Exceptions.ValidationException("Only approved clauses from the library can be added to the annex.");
        }
        template.SetAnnexClauses(annexIds);

        // Replace fields wholesale (designer save semantics), mirroring the HTML-template flow.
        foreach (var existing in template.Fields.ToList())
            db.PdfTemplateFields.Remove(existing);

        foreach (var f in command.Fields)
        {
            var field = PdfTemplateField.Create(
                template.Id,
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
                f.WorkflowStage);
            db.PdfTemplateFields.Add(field);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
