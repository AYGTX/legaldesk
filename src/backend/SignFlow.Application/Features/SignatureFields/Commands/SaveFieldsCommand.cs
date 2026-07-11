using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.SignatureFields.Commands;

public record SaveFieldsCommand(Guid DocumentId, List<FieldDto> Fields) : ICommand;

public record FieldDto(
    Guid? Id,
    FieldType FieldType,
    int Page,
    float X,
    float Y,
    float Width,
    float Height,
    float? Rotation,
    Guid? RecipientId,
    string? Label,
    string? Placeholder,
    string? DefaultValue,
    string? FontFamily,
    float? FontSize,
    string? FontColor,
    string? BorderColor,
    string? BackgroundColor,
    bool IsRequired = true,
    bool IsReadOnly = false,
    string? ValidationRule = null,
    string? Options = null,
    int ZIndex = 0,
    string? VisibilityRules = null,
    string? GroupName = null,
    string? Tooltip = null,
    int? MaxLength = null);

public class SaveFieldsCommandValidator : AbstractValidator<SaveFieldsCommand>
{
    public SaveFieldsCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.Fields).NotNull().NotEmpty().WithMessage("At least one field must be placed on the document.");
        RuleForEach(x => x.Fields).ChildRules(f =>
        {
            f.RuleFor(x => x.Page).GreaterThan(0);
            f.RuleFor(x => x.X).GreaterThanOrEqualTo(0);
            f.RuleFor(x => x.Y).GreaterThanOrEqualTo(0);
            f.RuleFor(x => x.Width).GreaterThan(0);
            f.RuleFor(x => x.Height).GreaterThan(0);
        });
    }
}

public class SaveFieldsCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser) : ICommandHandler<SaveFieldsCommand>
{
    public async Task HandleAsync(SaveFieldsCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.FindAsync([command.DocumentId], cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        if (document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        if (document.Status != DocumentStatus.Draft)
            throw new ConflictException("Fields can only be edited on draft documents.");

        var existingIds = command.Fields.Where(f => f.Id.HasValue).Select(f => f.Id!.Value).ToHashSet();
        var toDelete = await db.SignatureFields
            .Where(f => f.DocumentId == command.DocumentId && !existingIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        db.SignatureFields.RemoveRange(toDelete);

        foreach (var dto in command.Fields)
        {
            if (dto.Id.HasValue)
            {
                var existing = await db.SignatureFields.FindAsync([dto.Id.Value], cancellationToken);
                if (existing is not null && existing.DocumentId == command.DocumentId)
                {
                    existing.UpdatePosition(dto.Page, dto.X, dto.Y, dto.Width, dto.Height, dto.Rotation);
                    existing.UpdateStyle(dto.FontFamily, dto.FontSize, dto.FontColor, dto.BorderColor, dto.BackgroundColor);
                    existing.UpdateProperties(dto.Label, dto.Placeholder, dto.DefaultValue, dto.IsRequired, dto.IsReadOnly, dto.ValidationRule, dto.Tooltip, dto.MaxLength);
                    existing.SetZIndex(dto.ZIndex);
                    if (dto.Options is not null) existing.SetOptions(dto.Options);
                    if (dto.GroupName is not null) existing.SetGroupName(dto.GroupName);
                    if (dto.RecipientId.HasValue) existing.AssignToRecipient(dto.RecipientId.Value);
                    continue;
                }
            }

            var field = SignatureField.Create(command.DocumentId, dto.FieldType, dto.Page, dto.X, dto.Y, dto.Width, dto.Height, dto.RecipientId);
            field.UpdateStyle(dto.FontFamily, dto.FontSize, dto.FontColor, dto.BorderColor, dto.BackgroundColor);
            field.UpdateProperties(dto.Label, dto.Placeholder, dto.DefaultValue, dto.IsRequired, dto.IsReadOnly, dto.ValidationRule, dto.Tooltip, dto.MaxLength);
            field.SetZIndex(dto.ZIndex);
            if (dto.Options is not null) field.SetOptions(dto.Options);
            if (dto.GroupName is not null) field.SetGroupName(dto.GroupName);
            db.SignatureFields.Add(field);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
