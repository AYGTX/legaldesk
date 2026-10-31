using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.SignatureFields.Commands;

/// <param name="LayoutVersion">The layout the owner started from; a save made from an older one is refused (R-M3-4).
/// Null skips the check (scripts and older clients).</param>
public record SaveFieldsCommand(Guid DocumentId, List<FieldDto> Fields, int? LayoutVersion = null) : ICommand<int>;

/// <summary>A field in PDF points from the top-left of its page as displayed (R-M3-1).</summary>
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
    string? GroupName = null,
    string? Tooltip = null,
    int? MaxLength = null);

public class SaveFieldsCommandValidator : AbstractValidator<SaveFieldsCommand>
{
    public SaveFieldsCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        // An empty layout is valid: a circuit of approvers only has nothing to sign (R-M3-3).
        RuleFor(x => x.Fields).NotNull();
    }
}

/// <summary>
/// Saves a contract's field layout (M3). Checked before anything is written (R-M3-3): supported type, page exists, box
/// inside the page, participant of this contract, signature-type fields given to someone who signs, options for choice
/// fields. A save made from an older layout returns 409 (R-M3-4); the answer is the new layout version.
/// </summary>
public class SaveFieldsCommandHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IPdfService pdfService,
    ICurrentUserService currentUser) : ICommandHandler<SaveFieldsCommand, int>
{
    /// <summary>What the product supports end to end (R-M3-5).</summary>
    public static readonly IReadOnlySet<FieldType> Supported = new HashSet<FieldType>
    {
        FieldType.Signature, FieldType.Initials, FieldType.Stamp, FieldType.Date, FieldType.Name, FieldType.Email,
        FieldType.Company, FieldType.Title, FieldType.Text, FieldType.MultilineText, FieldType.Number, FieldType.Checkbox,
        FieldType.Radio, FieldType.Dropdown
    };

    private static readonly HashSet<FieldType> SignatureTypes = [FieldType.Signature, FieldType.Initials, FieldType.Stamp];

    /// <summary>Half a point of rounding is tolerated on the page edges.</summary>
    private const double Tolerance = 0.5;

    public async Task<int> HandleAsync(SaveFieldsCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.Recipients)
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        if (document.OwnerId != currentUser.UserId)
            throw new ForbiddenAccessException();

        if (document.Status != DocumentStatus.Draft)
            throw new ConflictException("Fields can only be edited on draft documents.");

        if (command.LayoutVersion is { } expected && expected != document.LayoutVersion)
            throw new ConflictException("layout.stale",
                "Someone saved this layout after you opened it. Reload it to see their changes, then place your fields again.");

        await ValidateAsync(document, command.Fields, cancellationToken);

        var existing = await db.SignatureFields
            .Where(f => f.DocumentId == command.DocumentId)
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        var keep = command.Fields.Where(f => f.Id.HasValue).Select(f => f.Id!.Value).ToHashSet();
        var toDelete = existing.Values.Where(f => !keep.Contains(f.Id)).ToList();

        // A document back in Draft after a revision upload can carry Signature rows from the
        // previous round — deleting their fields would cascade away that evidence. Such fields
        // can be moved and reassigned, but never removed.
        var toDeleteIds = toDelete.Select(f => f.Id).ToHashSet();
        if (toDeleteIds.Count > 0 && await db.Signatures.AnyAsync(s => toDeleteIds.Contains(s.SignatureFieldId), cancellationToken))
            throw new ConflictException(
                "Some fields were signed in a previous version of this document and can't be removed. " +
                "You can reposition or reassign them instead.");

        db.SignatureFields.RemoveRange(toDelete);

        foreach (var dto in command.Fields)
        {
            if (dto.Id is { } id && existing.TryGetValue(id, out var field))
            {
                field.UpdatePosition(dto.Page, dto.X, dto.Y, dto.Width, dto.Height, dto.Rotation);
            }
            else
            {
                field = SignatureField.Create(command.DocumentId, dto.FieldType, dto.Page, dto.X, dto.Y, dto.Width, dto.Height, dto.RecipientId);
                if (dto.Rotation is not null) field.UpdatePosition(dto.Page, dto.X, dto.Y, dto.Width, dto.Height, dto.Rotation);
                db.SignatureFields.Add(field);
            }

            field.UpdateStyle(dto.FontFamily, dto.FontSize, dto.FontColor, dto.BorderColor, dto.BackgroundColor);
            field.UpdateProperties(dto.Label, dto.Placeholder, dto.DefaultValue, dto.IsRequired, dto.IsReadOnly, dto.ValidationRule, dto.Tooltip, dto.MaxLength);
            if (dto.Options is not null) field.SetOptions(dto.Options);
            if (dto.GroupName is not null) field.SetGroupName(dto.GroupName);
            if (dto.RecipientId.HasValue) field.AssignToRecipient(dto.RecipientId.Value);
        }

        document.LayoutSaved();
        await db.SaveChangesAsync(cancellationToken);
        return document.LayoutVersion;
    }

    private async Task ValidateAsync(Document document, List<FieldDto> fields, CancellationToken ct)
    {
        if (fields.Count == 0) return;

        var participants = document.Recipients.ToDictionary(r => r.Id);
        var pages = await PageSizesAsync(document, ct);
        var errors = new List<string>();

        foreach (var (f, i) in fields.Select((f, i) => (f, i + 1)))
        {
            var name = string.IsNullOrWhiteSpace(f.Label) ? $"Field {i} ({f.FieldType})" : $"\"{f.Label}\"";

            if (!Supported.Contains(f.FieldType))
                errors.Add($"{name}: {f.FieldType} fields are not supported.");
            if (f.Width <= 0 || f.Height <= 0)
                errors.Add($"{name}: the box has no size.");
            if (f.Rotation is { } r && r is not (0 or 90 or 180 or 270))
                errors.Add($"{name}: rotation must be 0, 90, 180 or 270 degrees.");

            if (f.Page < 1 || f.Page > pages.Count)
                errors.Add($"{name}: page {f.Page} does not exist (the contract has {pages.Count}).");
            else
            {
                var page = pages[f.Page - 1];
                if (f.X < -Tolerance || f.Y < -Tolerance || f.X + f.Width > page.Width + Tolerance || f.Y + f.Height > page.Height + Tolerance)
                    errors.Add($"{name}: the box goes outside page {f.Page}.");
            }

            if (f.RecipientId is { } rid)
            {
                if (!participants.TryGetValue(rid, out var who))
                    errors.Add($"{name}: it is given to someone who is not a participant of this contract.");
                else if (SignatureTypes.Contains(f.FieldType) && who.ApprovalType is not (ApprovalType.Signature or ApprovalType.Stamp))
                    errors.Add($"{name}: {who.Name} reviews or approves; only a participant who signs or stamps can have a {f.FieldType} field.");
            }

            if (f.FieldType is FieldType.Dropdown or FieldType.Radio && string.IsNullOrWhiteSpace(f.Options) && string.IsNullOrWhiteSpace(f.DefaultValue))
                errors.Add($"{name}: a {f.FieldType} needs its options.");
        }

        if (errors.Count > 0)
            throw new Common.Exceptions.ValidationException(errors.Select(e => new FluentValidation.Results.ValidationFailure("fields", e)));
    }

    /// <summary>Page sizes as displayed, read from the stored file. A contract built from an HTML template has no file
    /// until it is signed: its one page is A4.</summary>
    private async Task<IReadOnlyList<PageSize>> PageSizesAsync(Document document, CancellationToken ct)
    {
        if (document.StoragePath.StartsWith("html-templates/", StringComparison.Ordinal))
            return [.. Enumerable.Repeat(new PageSize(595.28, 841.89), Math.Max(1, document.PageCount))];

        await using var stream = await storage.DownloadAsync(document.StoragePath, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        var sizes = await pdfService.GetPageSizesAsync(buffer, ct);
        return sizes.Count > 0 ? sizes : [.. Enumerable.Repeat(new PageSize(595.28, 841.89), Math.Max(1, document.PageCount))];
    }
}
