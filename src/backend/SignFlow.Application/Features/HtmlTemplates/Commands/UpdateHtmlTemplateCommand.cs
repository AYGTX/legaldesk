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
    Guid? DefaultApproverId,
    bool IsActive,
    List<HtmlTemplateFieldInput> Fields,
    WorkflowType WorkflowType = WorkflowType.Sequential,
    bool HideRecipientIdentities = false) : ICommand;

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
        if (!currentUser.IsInRole("Administrator") && !currentUser.IsInRole("Manager"))
            throw new ForbiddenAccessException();

        var template = await db.HtmlTemplates
            .Include(t => t.Fields)
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(HtmlTemplate), command.Id);

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

        template.Update(
            command.Name,
            command.HtmlContent,
            command.Description,
            command.DefaultApproverId,
            command.IsActive,
            command.WorkflowType,
            command.HideRecipientIdentities);

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
                f.PreAssignedApproverId,
                f.WorkflowStage);
            db.HtmlTemplateFields.Add(field);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
