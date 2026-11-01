using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.HtmlTemplates.Commands;

/// <summary>
/// One approver choice for a field with no fixed approver: either an existing system user (UserId),
/// or an external signer with no account — just a name and email, same as PDF-flow "Other" recipients.
/// External signers get a public /sign/{token} link, no login required.
/// </summary>
public record ApproverSelection(Guid? UserId, string? ExternalName, string? ExternalEmail);

public record CreateRequestFromHtmlTemplateCommand(
    Guid TemplateId,
    /// <summary>fieldKey → value filled by the initiator (Initiator-role fields).</summary>
    Dictionary<string, string> FilledValues,
    /// <summary>
    /// Approver choices for Approver Signature fields that have no field-level PreAssignedApprover and no
    /// template-wide DefaultApprover — consumed in field order. Fields with either of those already set
    /// ignore this list entirely.
    /// </summary>
    List<ApproverSelection>? Approvers = null,
    string? Title = null,
    string? Message = null) : ICommand<Guid>;

public class CreateRequestFromHtmlTemplateCommandValidator : AbstractValidator<CreateRequestFromHtmlTemplateCommand>
{
    public CreateRequestFromHtmlTemplateCommandValidator()
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

public class CreateRequestFromHtmlTemplateCommandHandler(
    IApplicationDbContext db,
    IWorkflowEngine workflowEngine,
    IWorkflowPresetResolver presets,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ILogger<CreateRequestFromHtmlTemplateCommandHandler> logger) : ICommandHandler<CreateRequestFromHtmlTemplateCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateRequestFromHtmlTemplateCommand command, CancellationToken cancellationToken = default)
    {
        var initiatorId = currentUser.UserId ?? throw new ForbiddenAccessException();

        var template = await db.HtmlTemplates
            .Include(t => t.Fields)
            .Include(t => t.DefaultApprover)
            .Include(t => t.RequestType)
            .FirstOrDefaultAsync(t => t.Id == command.TemplateId, cancellationToken)
            ?? throw new NotFoundException(nameof(HtmlTemplate), command.TemplateId);

        if (!template.IsActive)
            throw new ConflictException("This template is no longer active.");
        // Only a version legal approved makes contracts (R-M6-5).
        if (template.Status != TemplateStatus.Approved)
            throw new ConflictException("template.notApproved", "This template version has not been approved by legal yet.");

        var initiator = await db.Users.FindAsync([initiatorId], cancellationToken)
            ?? throw new ForbiddenAccessException();

        var title = command.Title ?? template.Name;

        // A blank answer takes the template's default; an auto-fill source then overwrites whatever the client sent.
        // Both come before the required check, so a field the app fills itself is never reported as missing.
        ApplyDefaults(template.Fields, command.FilledValues);
        ApplyAutoFill(template.Fields, command.FilledValues, initiator, title);

        TemplateAnswerRules.RequireListedOptions(
            template.Fields.Select(f => (f.FieldKey, f.Label, f.Type.ToString(), f.OptionsJson)),
            command.FilledValues);

        // Validate required initiator fields
        var requiredMissing = template.Fields
            .Where(f => f.Role == HtmlTemplateFieldRole.Initiator && f.IsRequired && f.Type != HtmlTemplateFieldType.Signature)
            .Where(f => !command.FilledValues.TryGetValue(f.FieldKey, out var v) || string.IsNullOrWhiteSpace(v))
            .Select(f => f.Label)
            .ToList();

        if (requiredMissing.Count > 0)
            throw new Common.Exceptions.ValidationException($"Required fields are missing: {string.Join(", ", requiredMissing)}");

        // Ordered Approver Signature fields from the template
        var approverSigFields = template.Fields
            .Where(f => f.Role == HtmlTemplateFieldRole.Approver && f.Type == HtmlTemplateFieldType.Signature)
            .OrderBy(f => f.Order)
            .ToList();

        // A workflow preset, when the template has one, names the signer of the first signature fields in order and
        // may drop a step whose condition does not hold for these answers — together with that step's signature field.
        var routing = await presets.ResolveAsync(
            template.WorkflowPresetId, approverSigFields.Count, command.FilledValues,
            template.Fields.ToDictionary(f => f.FieldKey, f => f.Type), cancellationToken);

        var presetFor = new List<PresetAssignment?>(approverSigFields.Count);
        if (routing is not null)
        {
            var kept = new List<HtmlTemplateField>();
            for (var i = 0; i < approverSigFields.Count; i++)
            {
                var assignment = i < routing.Assignments.Count ? routing.Assignments[i] : null;
                if (assignment is { Skipped: true }) continue;
                kept.Add(approverSigFields[i]);
                presetFor.Add(assignment);
            }

            if (kept.Count == 0)
                throw new Common.Exceptions.ValidationException(
                    $"Every approval step of the workflow \"{routing.PresetName}\" is skipped for these answers, so nobody would sign.");

            approverSigFields = kept;
        }
        else
        {
            presetFor.AddRange(approverSigFields.Select(_ => (PresetAssignment?)null));
        }

        var workflowType = routing?.WorkflowType ?? template.WorkflowType;

        // Resolve one approver selection per sig field. Precedence: the workflow preset's step > field-level
        // PreAssignedApprover (locked by the template admin, always a system user) > template-wide DefaultApprover
        // (also a system user) > caller-supplied Approvers (consumed in order, one per field that still needs a
        // choice — each can be a system user OR an external signer with no account).
        var supplied = command.Approvers ?? [];
        var suppliedIndex = 0;
        var selections = new List<ApproverSelection>(approverSigFields.Count);
        for (var fieldIndex = 0; fieldIndex < approverSigFields.Count; fieldIndex++)
        {
            var field = approverSigFields[fieldIndex];
            if (presetFor[fieldIndex]?.Pool is { } pool)
                selections.Add(new ApproverSelection(null, pool.Name, Recipient.PoolAddress));
            else if (presetFor[fieldIndex]?.Approver is { } presetApprover)
                selections.Add(presetApprover);
            else if (field.PreAssignedApproverId.HasValue)
                selections.Add(new ApproverSelection(field.PreAssignedApproverId, null, null));
            else if (template.DefaultApproverId.HasValue)
                selections.Add(new ApproverSelection(template.DefaultApproverId, null, null));
            else if (suppliedIndex < supplied.Count)
                selections.Add(supplied[suppliedIndex++]);
            else
                throw new Common.Exceptions.ValidationException("An approver must be specified for this template.");
        }

        // Resolve each selection to a concrete (Name, Email, Company) — looking up system users where
        // needed — so the recipient-creation loop below doesn't care whether a signer has an account.
        var userCache = new Dictionary<Guid, ApplicationUser>();
        var resolvedApprovers = new List<(string Name, string Email, string? Company)>(selections.Count);
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
                resolvedApprovers.Add((approver.FullName, approver.Email!, approver.Company));
            }
            else
            {
                resolvedApprovers.Add((selection.ExternalName!, selection.ExternalEmail!, null));
            }
        }

        var filledJson = JsonSerializer.Serialize(command.FilledValues);

        // Create document — use a placeholder path since there is no physical file yet
        var document = Document.Create(
            title,
            fileName: $"{template.Name}.html",
            storagePath: $"html-templates/{template.Id}",
            fileSizeBytes: 0,
            pageCount: 1,
            ownerId: initiatorId,
            description: template.Description,
            message: command.Message);

        document.SetHtmlSource(template.Id, filledJson, template.HtmlContent, template.Version);
        document.SetWorkflowType(workflowType);
        document.SetHideRecipientIdentities(template.HideRecipientIdentities);
        document.SetCreatedBy(initiatorId.ToString());

        // Inherit the template's request type and snapshot its SLA at creation time — later changes to
        // the type's SLA (or reassigning the template's type) never affect this in-flight request.
        if (template.RequestType is { } requestType)
            document.ApplyRequestType(requestType.Id, requestType.SlaHours);

        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        // Create one recipient per approver sig field (sequential workflow). Recipients aren't tied to a
        // user account — external signers get a public /sign/{token} link just like system users do.
        var firstApproverEmail = string.Empty;

        // Mixed stages, renumbered 1..n: a skipped step leaves a gap that nothing downstream should have to understand.
        var stageOf = approverSigFields.Select((f, i) => presetFor[i]?.Stage ?? f.WorkflowStage).ToList();
        var stageRank = stageOf.Distinct().Order().Select((stage, index) => (stage, rank: index + 1)).ToDictionary(x => x.stage, x => x.rank);

        for (var i = 0; i < resolvedApprovers.Count; i++)
        {
            var (approverName, approverEmail, approverCompany) = resolvedApprovers[i];

            var order = workflowType switch
            {
                WorkflowType.Parallel => 1,
                WorkflowType.Mixed when i < approverSigFields.Count => stageRank[stageOf[i]],
                _ => i + 1
            };

            var recipient = Recipient.Create(
                document.Id,
                approverName,
                approverEmail,
                order,
                ApprovalType.Signature,
                company: approverCompany,
                roleLabel: i < presetFor.Count ? presetFor[i]?.Label : null);
            // A team or role step: its members share it (R-M4-3).
            if (i < presetFor.Count && presetFor[i]?.Pool is { } recipientPool)
                recipient.SetCircuit(StageRule.All, recipientPool.TeamId, recipientPool.Role, null, 3, null, null);
            db.Recipients.Add(recipient);

            if (i == 0) firstApproverEmail = recipient.IsPool ? approverName : approverEmail;

            // One SignatureField per Approver Signature template field, assigned to this recipient
            if (i < approverSigFields.Count)
            {
                var sf = approverSigFields[i];
                var sigField = SignatureField.Create(
                    document.Id,
                    FieldType.Signature,
                    page: 1,
                    x: 0, y: 0, width: 200, height: 80,
                    recipientId: recipient.Id);
                sigField.UpdateProperties(sf.Label, null, null, sf.IsRequired, false, null, null, null);
                sigField.SetTemplateKey(sf.FieldKey);
                db.SignatureFields.Add(sigField);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Nobody is notified yet: every request made from a template is reviewed before its circuit starts (R-M6-6).
        // It opens on the review page, and is sent from there through the same gate as any contract.
        await auditService.LogAsync(
            document.Id,
            AuditAction.DocumentCreated,
            "system",
            $"Request '{title}' created from template '{template.Name}' v{template.Version}"
                + (routing is null ? "" : $" following the workflow '{routing.PresetName}'")
                + ", waiting for the pre-flight review before it is sent",
            initiatorId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        logger.LogInformation("HTML-template request {DocumentId} created from template {TemplateId}", document.Id, template.Id);

        return document.Id;
    }

    private static void ApplyDefaults(IEnumerable<HtmlTemplateField> fields, Dictionary<string, string> values)
    {
        foreach (var field in fields.Where(f => f.Role == HtmlTemplateFieldRole.Initiator
                                                 && f.Type != HtmlTemplateFieldType.Signature
                                                 && !string.IsNullOrEmpty(f.DefaultValue)))
        {
            if (!values.TryGetValue(field.FieldKey, out var current) || string.IsNullOrWhiteSpace(current))
                values[field.FieldKey] = field.DefaultValue!;
        }
    }

    private static void ApplyAutoFill(
        IEnumerable<HtmlTemplateField> fields,
        Dictionary<string, string> values,
        ApplicationUser user,
        string requestTitle)
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
                _ => TemplateAnswerRules.ResolveRequestSource(field.AutoFillFrom, requestTitle)
            };

            if (resolved is not null)
                values[field.FieldKey] = resolved;
        }
    }
}
