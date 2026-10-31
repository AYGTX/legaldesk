using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Documents.Commands;

/// <summary>
/// The owner answers the template's questions again after a reviewer asked for changes. The PDF, the field layout
/// and the approval circuit are the template's and do not change; only the answers do. The outgoing version is kept
/// together with its answers, the document becomes the next version, and the circuit starts again so the reviewer
/// who asked for changes sees the new answers next to the old.
/// </summary>
public record ReviseFromTemplateCommand(
    Guid DocumentId,
    Dictionary<string, string> FilledValues,
    string? ChangeNote) : ICommand<UploadRevisionResult>;

public class ReviseFromTemplateCommandValidator : AbstractValidator<ReviseFromTemplateCommand>
{
    public ReviseFromTemplateCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.FilledValues).NotNull();
        RuleForEach(x => x.FilledValues).Must(kv => (kv.Value ?? string.Empty).Length <= 10_000)
            .WithMessage("An answer is too long.");
        RuleFor(x => x.ChangeNote).MaximumLength(2000);
    }
}

public class ReviseFromTemplateCommandHandler(
    IApplicationDbContext db,
    IWorkflowEngine workflowEngine,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<ReviseFromTemplateCommandHandler> logger) : ICommandHandler<ReviseFromTemplateCommand, UploadRevisionResult>
{
    public async Task<UploadRevisionResult> HandleAsync(ReviseFromTemplateCommand command, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .Include(d => d.Recipients)
            .Include(d => d.SignatureFields)
            .Include(d => d.Workflow)
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId && !d.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), command.DocumentId);

        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();
        if (document.OwnerId != userId)
            throw new ForbiddenAccessException();

        var kind = TemplateRevisionSupport.KindOf(document)
            ?? throw new ConflictException("Only contracts built from a template are revised by changing their answers.");

        if (document.Status != DocumentStatus.ChangesRequested)
            throw new ConflictException("Answers can only be revised after a participant requested changes.");

        var fields = await TemplateRevisionSupport.LoadFieldsAsync(db, document, cancellationToken)
            ?? throw new ConflictException("The template this contract was built from no longer exists, so its answers cannot be revised.");

        var before = TemplateRevisionSupport.ParseValues(document.FilledValuesJson);
        var after = new Dictionary<string, string>(before);

        // Only the answers the owner may change are taken from the request; anything else — auto-filled values,
        // keys the template does not have — is ignored, so a crafted request cannot rewrite them.
        foreach (var field in fields.Where(f => f.Editable))
        {
            if (command.FilledValues.TryGetValue(field.Key, out var value))
                after[field.Key] = value ?? string.Empty;
        }

        TemplateAnswerRules.RequireListedOptions(
            fields.Select(f => (f.Key, f.Label, f.Type, f.OptionsJson)), after);

        var missing = fields
            .Where(f => f.Editable && f.IsRequired)
            .Where(f => !after.TryGetValue(f.Key, out var v) || string.IsNullOrWhiteSpace(v))
            .Select(f => f.Label)
            .ToList();
        if (missing.Count > 0)
            throw new Common.Exceptions.ValidationException($"Required fields are missing: {string.Join(", ", missing)}");

        var changes = TemplateRevisionSupport.Diff(before, after, fields);
        if (changes.Count == 0)
            throw new ConflictException("No answer was changed. Change at least one answer to send a new version.");

        var note = string.IsNullOrWhiteSpace(command.ChangeNote)
            ? $"Answers revised: {string.Join(", ", changes.Select(c => c.Label))}"
            : command.ChangeNote;

        // The outgoing version keeps the answers it had — that is what the reviewer compares against.
        var previous = DocumentVersion.Create(
            document.Id, document.Version, document.StoragePath, document.FileSizeBytes, userId, note);
        previous.SetFilledValues(document.FilledValuesJson);
        previous.SetCreatedBy(userId.ToString());
        db.DocumentVersions.Add(previous);

        if (kind == TemplateRevisionSupport.Pdf)
            await ApplyToPdfFieldsAsync(document, changes, cancellationToken);

        // Approver signatures are voided so everyone signs the new answers. Fields the owner filled keep their value.
        var voided = document.SignatureFields.Where(f => f.RecipientId.HasValue && f.IsSigned).ToList();
        foreach (var field in voided)
            field.VoidSignature();

        await CircuitRounds.CloseForRevisionAsync(db, document, cancellationToken);
        foreach (var recipient in document.Recipients)
            recipient.ResetForRevision();

        var oldVersion = document.Version;
        document.ApplyTemplateRevision(JsonSerializer.Serialize(after));
        await db.SaveChangesAsync(cancellationToken);

        if (voided.Count > 0)
        {
            await auditService.LogAsync(
                document.Id, AuditAction.SignaturesVoided, "system",
                $"{voided.Count} signed field(s) voided by an answers revision — signature records retained as evidence",
                userId, actorEmail: currentUser.UserEmail, cancellationToken: cancellationToken);
        }

        await auditService.LogAsync(
            document.Id, AuditAction.RevisionUploaded, "system",
            $"Answers revised (v{oldVersion} → v{document.Version}): {string.Join(", ", changes.Select(c => c.Label))}",
            userId, actorEmail: currentUser.UserEmail, cancellationToken: cancellationToken);

        // The circuit starts again, as it did when the request was first created from the template.
        await workflowEngine.StartAsync(document.Id, cancellationToken);

        await auditService.LogAsync(
            document.Id, AuditAction.DocumentSent, "system",
            $"Revised request (v{document.Version}) sent for approval again",
            userId, actorEmail: currentUser.UserEmail, cancellationToken: cancellationToken);

        logger.LogInformation("Template document {DocumentId} revised to v{Version} ({Count} answers changed)",
            document.Id, document.Version, changes.Count);

        return new UploadRevisionResult(document.Id, document.Version, document.PageCount);
    }

    /// <summary>
    /// A PDF template stamps the owner's answers into read-only fields on the instance. Each changed answer is
    /// written to the field the template placed at that spot (matched by page and position, because the instance
    /// fields do not carry the template's key).
    /// </summary>
    private async Task ApplyToPdfFieldsAsync(Document document, List<FieldChange> changes, CancellationToken cancellationToken)
    {
        var template = await db.PdfTemplates.AsNoTracking()
            .Include(t => t.Fields)
            .FirstAsync(t => t.Id == document.PdfTemplateId, cancellationToken);

        var taken = new HashSet<Guid>();
        foreach (var change in changes)
        {
            var templateField = template.Fields.FirstOrDefault(f => f.FieldKey == change.FieldKey);
            if (templateField is null) continue;

            var instance = document.SignatureFields.FirstOrDefault(f =>
                !taken.Contains(f.Id)
                && f.RecipientId is null
                && f.Page == templateField.Page
                && Math.Abs(f.X - templateField.X) < 0.5f
                && Math.Abs(f.Y - templateField.Y) < 0.5f
                && Math.Abs(f.Width - templateField.Width) < 0.5f
                && Math.Abs(f.Height - templateField.Height) < 0.5f);
            if (instance is null) continue;

            taken.Add(instance.Id);
            var value = change.NewValue ?? string.Empty;
            instance.UpdateProperties(instance.Label, instance.Placeholder, value, instance.IsRequired,
                instance.IsReadOnly, instance.ValidationRule, instance.Tooltip, instance.MaxLength);

            if (value.Length > 0) instance.SetSigned(value);
            else instance.VoidSignature();
        }
    }
}
