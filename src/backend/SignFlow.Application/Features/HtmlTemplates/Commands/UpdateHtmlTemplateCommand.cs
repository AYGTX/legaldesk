using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.HtmlTemplates.Commands;

public record UpdateHtmlTemplateCommand(
    Guid Id,
    string Name,
    string HtmlContent,
    string? Description,
    bool IsActive,
    List<HtmlTemplateFieldInput> Fields,
    WorkflowType WorkflowType = WorkflowType.Sequential,
    bool HideRecipientIdentities = false,
    Guid? RequestTypeId = null,
    Guid? WorkflowPresetId = null) : ICommand;

public class UpdateHtmlTemplateCommandValidator : AbstractValidator<UpdateHtmlTemplateCommand>
{
    public UpdateHtmlTemplateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.HtmlContent).NotEmpty();
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
    }
}

public class UpdateHtmlTemplateCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<UpdateHtmlTemplateCommand>
{
    public async Task HandleAsync(UpdateHtmlTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageTemplates))
            throw new ForbiddenAccessException();

        var template = await db.HtmlTemplates
            .Include(t => t.Fields)
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(HtmlTemplate), command.Id);

        // An approved or retired version is never edited (R-M6-5): its next version is drafted instead.
        if (!template.IsEditable)
            throw new ConflictException("template.notEditable", "This version is approved: draft its next version to change it.");


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

        // Library clauses keep their approved wording (R-M6-7).
        await Common.Services.LockedClauses.CheckAsync(db, command.HtmlContent, cancellationToken);

        template.Update(
            command.Name,
            // Cleaned on save (R-M6-3): what is stored never runs in a reader's browser or makes the renderer fetch a URL.
            Common.Services.SafeHtml.Clean(command.HtmlContent),
            command.Description,
            command.IsActive,
            command.WorkflowType,
            command.HideRecipientIdentities,
            command.RequestTypeId);
        template.SetWorkflowPreset(command.WorkflowPresetId);

        // Replace fields: remove old ones and add new ones
        var existingFields = template.Fields.ToList();
        foreach (var existing in existingFields)
            db.HtmlTemplateFields.Remove(existing);

        foreach (var f in command.Fields)
        {
            var field = HtmlTemplateField.Create(
                template.Id,
                f.FieldKey,
                f.Label,
                f.Type,
                f.Role,
                f.IsRequired,
                f.Order,
                f.AutoFillFrom,
                f.OptionsJson,
                f.FilledByStep,
                f.DefaultValue);
            db.HtmlTemplateFields.Add(field);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
