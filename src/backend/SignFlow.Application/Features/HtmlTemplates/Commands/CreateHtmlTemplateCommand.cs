using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.HtmlTemplates.Commands;

public record HtmlTemplateFieldInput(
    string FieldKey,
    string Label,
    HtmlTemplateFieldType Type,
    HtmlTemplateFieldRole Role,
    bool IsRequired,
    int Order,
    string? AutoFillFrom = null,
    string? OptionsJson = null,
    int FilledByStep = 1,
    string? DefaultValue = null);

public record CreateHtmlTemplateCommand(
    string Name,
    string HtmlContent,
    string? Description = null,
    List<HtmlTemplateFieldInput>? Fields = null,
    WorkflowType WorkflowType = WorkflowType.Sequential,
    bool HideRecipientIdentities = false,
    /// <summary>
    /// Request type this template carries. Requests created from it inherit this type (and its SLA) at
    /// creation time, as a snapshot — changing the type's SLA later never affects in-flight requests.
    /// </summary>
    Guid? RequestTypeId = null,
    /// <summary>The reusable approval workflow this template follows, if any.</summary>
    Guid? WorkflowPresetId = null) : ICommand<Guid>;

public class CreateHtmlTemplateCommandValidator : AbstractValidator<CreateHtmlTemplateCommand>
{
    public CreateHtmlTemplateCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.HtmlContent).NotEmpty();
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
    }
}

public class CreateHtmlTemplateCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<CreateHtmlTemplateCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateHtmlTemplateCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageTemplates))
            throw new ForbiddenAccessException();

        var createdById = currentUser.UserId ?? throw new ForbiddenAccessException();


        if (command.RequestTypeId.HasValue)
        {
            var requestTypeExists = await db.RequestTypes
                .AnyAsync(t => t.Id == command.RequestTypeId.Value && t.IsActive, cancellationToken);
            if (!requestTypeExists)
                throw new Common.Exceptions.ValidationException("The selected request type is invalid or inactive.");
        }

        if (command.WorkflowPresetId.HasValue
            && !await db.WorkflowPresets.AnyAsync(p => p.Id == command.WorkflowPresetId.Value && p.IsActive, cancellationToken))
            throw new Common.Exceptions.ValidationException("The selected workflow is invalid or inactive.");

        var template = HtmlTemplate.Create(
            command.Name,
            // Cleaned on save (R-M6-3): what is stored never runs in a reader's browser or makes the renderer fetch a URL.
            Common.Services.SafeHtml.Clean(command.HtmlContent),
            createdById,
            command.Description,
            command.WorkflowType,
            command.HideRecipientIdentities,
            command.RequestTypeId);
        template.SetWorkflowPreset(command.WorkflowPresetId);

        db.HtmlTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        if (command.Fields is { Count: > 0 })
        {
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

        return template.Id;
    }
}
