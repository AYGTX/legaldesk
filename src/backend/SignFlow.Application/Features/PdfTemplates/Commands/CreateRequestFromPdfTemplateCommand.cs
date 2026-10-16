using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.HtmlTemplates.Commands; // reuse ApproverSelection
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.PdfTemplates.Commands;

/// <summary>
/// Creates a request (document instance) from a persisted PDF template. The stored base PDF is copied
/// per instance (never regenerated), each configured field is materialized as a <see cref="SignatureField"/>
/// at its coordinates, requester/system fields are pre-filled (with auto-fill applied), and approver
/// fields are routed to recipients — which lets the existing signing + stamping pipeline handle the rest.
/// </summary>
public record CreateRequestFromPdfTemplateCommand(
    Guid TemplateId,
    /// <summary>fieldKey → value filled by the initiator (Requester-role fields).</summary>
    Dictionary<string, string> FilledValues,
    /// <summary>
    /// Approver choices for Approver signature fields with no field-level PreAssignedApprover and no
    /// template-wide DefaultApprover — consumed in field order. Each may be a system user or an external signer.
    /// </summary>
    List<ApproverSelection>? Approvers = null,
    string? Title = null,
    string? Message = null) : ICommand<Guid>;

public class CreateRequestFromPdfTemplateCommandValidator : AbstractValidator<CreateRequestFromPdfTemplateCommand>
{
    public CreateRequestFromPdfTemplateCommandValidator()
    {
        RuleFor(x => x.TemplateId).NotEmpty();
        RuleFor(x => x.FilledValues).NotNull();
        RuleForEach(x => x.Approvers).ChildRules(a =>
        {
            a.RuleFor(x => x).Must(x => x.UserId.HasValue || (!string.IsNullOrWhiteSpace(x.ExternalName) && !string.IsNullOrWhiteSpace(x.ExternalEmail)))
                .WithMessage("Each approver must be either an existing user or have a name and email.");
            a.RuleFor(x => x.ExternalEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.ExternalEmail));
        });
    }
}

public class CreateRequestFromPdfTemplateCommandHandler(
    IApplicationDbContext db,
    IStorageService storage,
    IWorkflowEngine workflowEngine,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<CreateRequestFromPdfTemplateCommandHandler> logger) : ICommandHandler<CreateRequestFromPdfTemplateCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateRequestFromPdfTemplateCommand command, CancellationToken cancellationToken = default)
    {
        var initiatorId = currentUser.UserId ?? throw new ForbiddenAccessException();

        var template = await db.PdfTemplates
            .Include(t => t.Fields)
            .Include(t => t.DefaultApprover)
            .Include(t => t.RequestType)
            .FirstOrDefaultAsync(t => t.Id == command.TemplateId, cancellationToken)
            ?? throw new NotFoundException(nameof(PdfTemplate), command.TemplateId);

        if (!template.IsActive)
            throw new ConflictException("This template is no longer active.");

        var initiator = await db.Users.FindAsync([initiatorId], cancellationToken)
            ?? throw new ForbiddenAccessException();

        var title = command.Title ?? template.Name;

        // Auto-fill first so required-field validation sees system-populated values too.
        ApplyAutoFill(template.Fields, command.FilledValues, initiator, title);

        TemplateAnswerRules.RequireListedOptions(
            template.Fields.Select(f => (f.FieldKey, f.Label, f.Type.ToString(), f.OptionsJson)),
            command.FilledValues);

        // Validate required requester fields (signature fields are filled during signing, not here).
        var requiredMissing = template.Fields
            .Where(f => f.FillRole == PdfFieldFillRole.Requester && f.IsRequired && f.Type != PdfFieldType.Signature)
            .Where(f => !command.FilledValues.TryGetValue(f.FieldKey, out var v) || string.IsNullOrWhiteSpace(v))
            .Select(f => f.Label)
            .ToList();
        if (requiredMissing.Count > 0)
            throw new Common.Exceptions.ValidationException($"Required fields are missing: {string.Join(", ", requiredMissing)}");

        // Ordered approver signature fields — each defines one recipient (mirrors the HTML flow).
        var approverSigFields = template.Fields
            .Where(f => f.FillRole == PdfFieldFillRole.Approver && f.Type == PdfFieldType.Signature)
            .OrderBy(f => f.Order)
            .ToList();

        if (approverSigFields.Count == 0)
            throw new Common.Exceptions.ValidationException("This template has no approver signature field, so it cannot be sent for signing.");

        // Resolve one approver per signature field: field PreAssignedApprover > template DefaultApprover > supplied list.
        var resolvedApprovers = await ResolveApproversAsync(template, approverSigFields, command.Approvers ?? [], cancellationToken);

        // Copy the persisted base PDF into the document store so each instance has its own stampable file
        // while the template's original stays pristine and reused for every instance.
        var destinationPath = $"documents/{initiatorId}/{Path.GetFileNameWithoutExtension(template.FileName)}_{Guid.NewGuid():N}.pdf";
        await storage.CopyAsync(template.StoragePath, destinationPath, cancellationToken);

        var document = Document.Create(
            title,
            fileName: template.FileName,
            storagePath: destinationPath,
            fileSizeBytes: template.FileSizeBytes,
            pageCount: template.PageCount,
            ownerId: initiatorId,
            description: template.Description,
            message: command.Message);

        document.SetPdfTemplateSource(template.Id, JsonSerializer.Serialize(command.FilledValues));
        document.SetWorkflowType(template.WorkflowType);
        document.SetHideRecipientIdentities(template.HideRecipientIdentities);
        document.SetCreatedBy(initiatorId.ToString());

        // Inherit the template's request type and snapshot its SLA at creation time — later changes to
        // the type's SLA (or reassigning the template's type) never affect this in-flight request.
        if (template.RequestType is { } requestType)
            document.ApplyRequestType(requestType.Id, requestType.SlaHours);

        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        // One recipient per approver signature field; index by workflow stage so an approver's other
        // (non-signature) fields can be routed to the same recipient.
        var sigFieldRecipient = new Dictionary<Guid, Recipient>();
        var stageToRecipient = new Dictionary<int, Recipient>();
        var firstApproverEmail = string.Empty;

        for (var i = 0; i < approverSigFields.Count; i++)
        {
            var sf = approverSigFields[i];
            var (name, email, company) = resolvedApprovers[i];

            var order = template.WorkflowType switch
            {
                WorkflowType.Parallel => 1,
                WorkflowType.Mixed => sf.WorkflowStage,
                _ => i + 1
            };

            var recipient = Recipient.Create(document.Id, name, email, order, ApprovalType.Signature, company: company);
            db.Recipients.Add(recipient);
            sigFieldRecipient[sf.Id] = recipient;
            stageToRecipient.TryAdd(sf.WorkflowStage, recipient);
            if (i == 0) firstApproverEmail = email;
        }

        // Materialize every configured field onto the instance document at its coordinates.
        foreach (var f in template.Fields)
        {
            Guid? recipientId = null;
            var isReadOnly = false;
            string? presetValue = null;

            if (f.FillRole == PdfFieldFillRole.Approver)
            {
                // Signature fields use their own recipient; other approver fields follow the same-stage recipient.
                var recipient = f.Type == PdfFieldType.Signature && sigFieldRecipient.TryGetValue(f.Id, out var own)
                    ? own
                    : stageToRecipient.GetValueOrDefault(f.WorkflowStage) ?? stageToRecipient.Values.FirstOrDefault();
                recipientId = recipient?.Id;
            }
            else
            {
                // Requester/System fields are filled now and rendered read-only.
                isReadOnly = true;
                command.FilledValues.TryGetValue(f.FieldKey, out presetValue);
                if (string.IsNullOrEmpty(presetValue)) presetValue = f.DefaultValue;
            }

            var field = SignatureField.Create(
                document.Id,
                f.ToSignatureFieldType(),
                f.Page,
                f.X, f.Y, f.Width, f.Height,
                recipientId);
            field.UpdateProperties(f.Label, placeholder: null, defaultValue: presetValue,
                isRequired: f.IsRequired, isReadOnly: isReadOnly, validationRule: null, tooltip: null, maxLength: null);
            if (!string.IsNullOrEmpty(f.OptionsJson))
                field.SetOptions(f.OptionsJson);
            if (isReadOnly && !string.IsNullOrEmpty(presetValue))
                field.SetSigned(presetValue);

            db.SignatureFields.Add(field);
        }

        await db.SaveChangesAsync(cancellationToken);

        await workflowEngine.StartAsync(document.Id, cancellationToken);

        await auditService.LogAsync(
            document.Id,
            AuditAction.DocumentSent,
            "system",
            $"Request '{title}' created from PDF template '{template.Name}' and sent to {firstApproverEmail}",
            initiatorId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        logger.LogInformation("PDF-template request {DocumentId} created from template {TemplateId}", document.Id, template.Id);

        return document.Id;
    }

    private async Task<List<(string Name, string Email, string? Company)>> ResolveApproversAsync(
        PdfTemplate template,
        List<PdfTemplateField> approverSigFields,
        List<ApproverSelection> supplied,
        CancellationToken cancellationToken)
    {
        var suppliedIndex = 0;
        var selections = new List<ApproverSelection>(approverSigFields.Count);
        foreach (var field in approverSigFields)
        {
            if (field.PreAssignedApproverId.HasValue)
                selections.Add(new ApproverSelection(field.PreAssignedApproverId, null, null));
            else if (template.DefaultApproverId.HasValue)
                selections.Add(new ApproverSelection(template.DefaultApproverId, null, null));
            else if (suppliedIndex < supplied.Count)
                selections.Add(supplied[suppliedIndex++]);
            else
                throw new Common.Exceptions.ValidationException("An approver must be specified for this template.");
        }

        var userCache = new Dictionary<Guid, ApplicationUser>();
        var resolved = new List<(string Name, string Email, string? Company)>(selections.Count);
        foreach (var selection in selections)
        {
            if (selection.UserId.HasValue)
            {
                if (!userCache.TryGetValue(selection.UserId.Value, out var approver))
                {
                    approver = await db.Users.FindAsync([selection.UserId.Value], cancellationToken)
                        ?? throw new NotFoundException(nameof(ApplicationUser), selection.UserId.Value);
                    userCache[selection.UserId.Value] = approver;
                }
                resolved.Add((approver.FullName, approver.Email!, approver.Company));
            }
            else
            {
                resolved.Add((selection.ExternalName!, selection.ExternalEmail!, null));
            }
        }

        return resolved;
    }

    private static void ApplyAutoFill(
        IEnumerable<PdfTemplateField> fields,
        Dictionary<string, string> values,
        ApplicationUser user,
        string title)
    {
        foreach (var field in fields.Where(f => !string.IsNullOrEmpty(f.AutoFillFrom)))
        {
            var resolved = field.AutoFillFrom switch
            {
                "user.fullName" => user.FullName,
                "user.firstName" => user.FirstName,
                "user.lastName" => user.LastName,
                "user.email" => user.Email ?? string.Empty,
                "user.company" => user.Company ?? string.Empty,
                "user.department" => user.Department ?? string.Empty,
                _ => TemplateAnswerRules.ResolveRequestSource(field.AutoFillFrom, title)
            };

            if (resolved is not null)
                values[field.FieldKey] = resolved;
        }
    }
}
