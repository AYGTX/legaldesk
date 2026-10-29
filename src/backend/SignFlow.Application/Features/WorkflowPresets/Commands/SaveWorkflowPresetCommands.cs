using SignFlow.Application.Common.Security;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.WorkflowPresets.Commands;

public record CreateWorkflowPresetCommand(
    string Name,
    string? Description,
    WorkflowType WorkflowType,
    List<WorkflowPresetStepInput> Steps) : ICommand<Guid>;

public record UpdateWorkflowPresetCommand(
    Guid Id,
    string Name,
    string? Description,
    WorkflowType WorkflowType,
    bool IsActive,
    List<WorkflowPresetStepInput> Steps) : ICommand;

internal static class WorkflowPresetRules
{
    public const int MaxSteps = 20;

    /// <summary>The single comparisons a step condition may use: the same set a conditional clause supports.</summary>
    public static readonly string[] Operators = ["==", "!=", ">=", "<=", ">", "<"];

    public static void AddStepRules<T>(AbstractValidator<T> validator, Func<T, IEnumerable<WorkflowPresetStepInput>> steps)
    {
        validator.RuleFor(x => steps(x).Count()).InclusiveBetween(1, MaxSteps)
            .WithMessage($"A workflow needs between 1 and {MaxSteps} steps.");
    }

    /// <summary>
    /// Checks the preset's own fields and then every step against the people and roles that exist, so a preset can
    /// never be saved half-broken. Done here as well as in the validators because the handler is the last line before
    /// the database, whatever the pipeline does or does not run in front of it.
    /// </summary>
    public static async Task ValidateAsync(
        string name,
        string? description,
        IReadOnlyList<WorkflowPresetStepInput> steps,
        IApplicationDbContext db,
        RoleManager<IdentityRole<Guid>> roles,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new Common.Exceptions.ValidationException("A workflow needs a name of at most 200 characters.");
        if (description is { Length: > 1000 })
            throw new Common.Exceptions.ValidationException("The description is limited to 1000 characters.");
        if (steps.Count is < 1 or > MaxSteps)
            throw new Common.Exceptions.ValidationException($"A workflow needs between 1 and {MaxSteps} steps.");

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var where = $"Step {i + 1}";

            switch (step.Kind)
            {
                case PresetApproverKind.User:
                    if (step.UserId is not { } userId || !await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct))
                        throw new Common.Exceptions.ValidationException($"{where}: choose an active person.");
                    break;
                case PresetApproverKind.Role:
                    if (string.IsNullOrWhiteSpace(step.RoleName) || !await roles.RoleExistsAsync(step.RoleName))
                        throw new Common.Exceptions.ValidationException($"{where}: choose an existing role.");
                    break;
                case PresetApproverKind.Department:
                    if (string.IsNullOrWhiteSpace(step.Department))
                        throw new Common.Exceptions.ValidationException($"{where}: enter a department.");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(step.ConditionFieldKey))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(step.ConditionFieldKey, @"^\w+$"))
                    throw new Common.Exceptions.ValidationException($"{where}: the condition's field key may only use letters, digits and underscores.");
                if (!string.IsNullOrWhiteSpace(step.ConditionOperator)
                    && (!Operators.Contains(step.ConditionOperator) || string.IsNullOrWhiteSpace(step.ConditionValue)))
                    throw new Common.Exceptions.ValidationException($"{where}: the condition needs one of ==, !=, >, >=, <, <= and a value.");
            }
        }
    }

    public static IEnumerable<WorkflowPresetStep> BuildSteps(Guid presetId, IReadOnlyList<WorkflowPresetStepInput> steps)
        => steps.Select((s, i) => WorkflowPresetStep.Create(
            presetId, i + 1, s.Stage,
            string.IsNullOrWhiteSpace(s.Label) ? null : s.Label.Trim(),
            s.Kind, s.UserId,
            s.RoleName?.Trim(), s.Department?.Trim(),
            string.IsNullOrWhiteSpace(s.ConditionFieldKey) ? null : s.ConditionFieldKey.Trim(),
            string.IsNullOrWhiteSpace(s.ConditionOperator) ? null : s.ConditionOperator.Trim(),
            string.IsNullOrWhiteSpace(s.ConditionValue) ? null : s.ConditionValue.Trim()));
}

public class CreateWorkflowPresetCommandValidator : AbstractValidator<CreateWorkflowPresetCommand>
{
    public CreateWorkflowPresetCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        WorkflowPresetRules.AddStepRules(this, x => x.Steps);
    }
}

public class UpdateWorkflowPresetCommandValidator : AbstractValidator<UpdateWorkflowPresetCommand>
{
    public UpdateWorkflowPresetCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        WorkflowPresetRules.AddStepRules(this, x => x.Steps);
    }
}

public class CreateWorkflowPresetCommandHandler(
    IApplicationDbContext db,
    RoleManager<IdentityRole<Guid>> roles,
    ICurrentUserService currentUser) : ICommandHandler<CreateWorkflowPresetCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateWorkflowPresetCommand command, CancellationToken cancellationToken = default)
    {
        // A workflow decides who approves what, so only administrators define one.
        if (!currentUser.Can(Permissions.ManageWorkflowPresets)) throw new ForbiddenAccessException();

        if (await db.WorkflowPresets.AnyAsync(p => p.Name == command.Name, cancellationToken))
            throw new ConflictException($"A workflow named \"{command.Name}\" already exists.");

        await WorkflowPresetRules.ValidateAsync(command.Name, command.Description, command.Steps, db, roles, cancellationToken);

        var preset = WorkflowPreset.Create(command.Name.Trim(), command.Description, command.WorkflowType);
        if (currentUser.UserId is { } userId) preset.SetCreatedBy(userId.ToString());
        db.WorkflowPresets.Add(preset);
        foreach (var step in WorkflowPresetRules.BuildSteps(preset.Id, command.Steps))
            db.WorkflowPresetSteps.Add(step);

        await db.SaveChangesAsync(cancellationToken);
        return preset.Id;
    }
}

public class UpdateWorkflowPresetCommandHandler(
    IApplicationDbContext db,
    RoleManager<IdentityRole<Guid>> roles,
    ICurrentUserService currentUser) : ICommandHandler<UpdateWorkflowPresetCommand>
{
    public async Task HandleAsync(UpdateWorkflowPresetCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageWorkflowPresets)) throw new ForbiddenAccessException();

        var preset = await db.WorkflowPresets.Include(p => p.Steps)
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowPreset), command.Id);

        if (await db.WorkflowPresets.AnyAsync(p => p.Name == command.Name && p.Id != command.Id, cancellationToken))
            throw new ConflictException($"A workflow named \"{command.Name}\" already exists.");

        await WorkflowPresetRules.ValidateAsync(command.Name, command.Description, command.Steps, db, roles, cancellationToken);

        preset.Update(command.Name.Trim(), command.Description, command.WorkflowType, command.IsActive);
        if (currentUser.UserId is { } userId) preset.SetUpdatedBy(userId.ToString());

        // Requests already created keep the people they were routed to; only requests created from now on follow the change.
        foreach (var old in preset.Steps.ToList()) db.WorkflowPresetSteps.Remove(old);
        foreach (var step in WorkflowPresetRules.BuildSteps(preset.Id, command.Steps))
            db.WorkflowPresetSteps.Add(step);

        await db.SaveChangesAsync(cancellationToken);
    }
}

public record DeleteWorkflowPresetCommand(Guid Id) : ICommand;

public class DeleteWorkflowPresetCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<DeleteWorkflowPresetCommand>
{
    public async Task HandleAsync(DeleteWorkflowPresetCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageWorkflowPresets)) throw new ForbiddenAccessException();

        var preset = await db.WorkflowPresets.FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowPreset), command.Id);

        var used = await db.HtmlTemplates.CountAsync(t => t.WorkflowPresetId == command.Id && !t.IsDeleted, cancellationToken)
                 + await db.PdfTemplates.CountAsync(t => t.WorkflowPresetId == command.Id && !t.IsDeleted, cancellationToken);
        if (used > 0)
            throw new ConflictException(
                $"{used} template(s) still follow this workflow. Move them to another workflow first, or deactivate this one.");

        db.WorkflowPresets.Remove(preset);
        await db.SaveChangesAsync(cancellationToken);
    }
}
