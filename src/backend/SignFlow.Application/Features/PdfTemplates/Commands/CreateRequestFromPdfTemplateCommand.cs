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
    string? Message = null,
    /// <summary>Library clauses the requester adds, appended as an annex after any the template always carries.</summary>
    List<Guid>? ClauseIds = null,
    /// <summary>
    /// False keeps the request a draft: nobody is notified until the owner has run the pre-flight review and sends it.
    /// </summary>
    bool StartImmediately = true) : ICommand<Guid>;

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
    IPdfService pdfService,
    IHtmlTemplatePdfGenerator htmlPdf,
    IWorkflowEngine workflowEngine,
    IWorkflowPresetResolver presets,
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

        // A workflow preset, when the template has one, names the signer of the first signature fields in order and
        // may drop a step whose condition does not hold for these answers, together with that step's signature field.
        var routing = await presets.ResolveAsync(
            template.WorkflowPresetId, approverSigFields.Count, command.FilledValues,
            template.Fields.ToDictionary(f => f.FieldKey, f => Enum.Parse<HtmlTemplateFieldType>(f.Type.ToString())),
            cancellationToken);

        var presetFor = new List<PresetAssignment?>(approverSigFields.Count);
        var skippedFieldIds = new HashSet<Guid>();
        if (routing is not null)
        {
            var kept = new List<PdfTemplateField>();
            for (var i = 0; i < approverSigFields.Count; i++)
            {
                var assignment = i < routing.Assignments.Count ? routing.Assignments[i] : null;
                if (assignment is { Skipped: true }) { skippedFieldIds.Add(approverSigFields[i].Id); continue; }
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

        // Resolve one approver per signature field: workflow step > field PreAssignedApprover > template DefaultApprover > supplied list.
        var resolvedApprovers = await ResolveApproversAsync(template, approverSigFields, presetFor, command.Approvers ?? [], cancellationToken);

        // Clauses the template always carries, then the ones the requester added: approved wording only.
        var annexClauses = await LoadAnnexClausesAsync(template, command.ClauseIds, cancellationToken);

        // Each instance gets its own stampable file in the document store while the template's original stays pristine
        // and reused for every instance. With an annex the copy is the base PDF followed by the clause pages.
        string destinationPath;
        var fileSize = template.FileSizeBytes;
        var pageCount = template.PageCount;
        if (annexClauses.Count == 0)
        {
            destinationPath = $"documents/{initiatorId}/{Path.GetFileNameWithoutExtension(template.FileName)}_{Guid.NewGuid():N}.pdf";
            await storage.CopyAsync(template.StoragePath, destinationPath, cancellationToken);
        }
        else
        {
            var annexHtml = PdfClauseAnnex.BuildHtml(
                [.. annexClauses.Select(c => new PdfClauseAnnex.AnnexClause(c.Title, c.Body, c.Language))],
                command.FilledValues,
                template.Fields.ToDictionary(f => f.FieldKey, f => Enum.Parse<HtmlTemplateFieldType>(f.Type.ToString())));

            await using var annexPdf = await htmlPdf.GenerateAsync(annexHtml, "Annex", [], cancellationToken);
            await using var basePdf = await storage.DownloadAsync(template.StoragePath, cancellationToken);
            await using var merged = await pdfService.MergePdfsAsync([basePdf, annexPdf], cancellationToken);

            // A failed merge must stop the request: an empty stream would be stored as the contract.
            if (!merged.CanSeek || merged.Length == 0)
                throw new InvalidOperationException("The contract could not be assembled with its annex.");

            merged.Position = 0;
            pageCount = await pdfService.GetPageCountAsync(merged, cancellationToken);
            if (merged.CanSeek) merged.Position = 0;
            fileSize = merged.Length;

            destinationPath = await storage.UploadAsync(
                merged, $"{Path.GetFileNameWithoutExtension(template.FileName)}_{Guid.NewGuid():N}.pdf",
                "application/pdf", $"documents/{initiatorId}", cancellationToken);
        }

        var document = Document.Create(
            title,
            fileName: template.FileName,
            storagePath: destinationPath,
            fileSizeBytes: fileSize,
            pageCount: pageCount,
            ownerId: initiatorId,
            description: template.Description,
            message: command.Message);

        document.SetPdfTemplateSource(template.Id, JsonSerializer.Serialize(command.FilledValues));
        document.SetWorkflowType(workflowType);
        document.SetHideRecipientIdentities(template.HideRecipientIdentities);
        document.SetCreatedBy(initiatorId.ToString());

        // Inherit the template's request type and snapshot its SLA at creation time — later changes to
        // the type's SLA (or reassigning the template's type) never affect this in-flight request.
        if (template.RequestType is { } requestType)
            document.ApplyRequestType(requestType.Id, requestType.SlaHours);

        foreach (var clause in annexClauses) clause.RecordUsage();

        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        // One recipient per approver signature field; index by workflow stage so an approver's other
        // (non-signature) fields can be routed to the same recipient.
        var sigFieldRecipient = new Dictionary<Guid, Recipient>();
        var stageToRecipient = new Dictionary<int, Recipient>();
        var firstApproverEmail = string.Empty;

        // Mixed stages, renumbered 1..n: a skipped step leaves a gap that nothing downstream should have to understand.
        var stageOf = approverSigFields.Select((f, i) => presetFor[i]?.Stage ?? f.WorkflowStage).ToList();
        var stageRank = stageOf.Distinct().Order().Select((stage, index) => (stage, rank: index + 1)).ToDictionary(x => x.stage, x => x.rank);

        for (var i = 0; i < approverSigFields.Count; i++)
        {
            var sf = approverSigFields[i];
            var (name, email, company) = resolvedApprovers[i];

            var order = workflowType switch
            {
                WorkflowType.Parallel => 1,
                WorkflowType.Mixed => stageRank[stageOf[i]],
                _ => i + 1
            };

            var recipient = Recipient.Create(document.Id, name, email, order, ApprovalType.Signature, company: company, roleLabel: presetFor[i]?.Label);
            // A team or role step: its members share it (R-M4-3).
            if (i < presetFor.Count && presetFor[i]?.Pool is { } recipientPool)
                recipient.SetCircuit(StageRule.All, recipientPool.TeamId, recipientPool.Role, null, 3, null, null);
            db.Recipients.Add(recipient);
            sigFieldRecipient[sf.Id] = recipient;
            stageToRecipient.TryAdd(stageOf[i], recipient);
            if (i == 0) firstApproverEmail = recipient.IsPool ? name : email;
        }

        // Materialize every configured field onto the instance document at its coordinates.
        foreach (var f in template.Fields)
        {
            if (skippedFieldIds.Contains(f.Id)) continue; // its workflow step does not apply to this request

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

        if (command.StartImmediately)
            await workflowEngine.StartAsync(document.Id, cancellationToken);

        await auditService.LogAsync(
            document.Id,
            command.StartImmediately ? AuditAction.DocumentSent : AuditAction.DocumentCreated,
            "system",
            $"Request '{title}' created from PDF template '{template.Name}'"
                + (routing is null ? "" : $" following the workflow '{routing.PresetName}'")
                + (annexClauses.Count == 0 ? "" : $", with {annexClauses.Count} annexed clause(s): {string.Join("; ", annexClauses.Select(c => $"{c.Title} v{c.Version}"))}")
                + (command.StartImmediately ? $" and sent to {firstApproverEmail}" : ", waiting for the pre-flight review before it is sent"),
            initiatorId,
            actorEmail: currentUser.UserEmail,
            cancellationToken: cancellationToken);

        logger.LogInformation("PDF-template request {DocumentId} created from template {TemplateId}", document.Id, template.Id);

        return document.Id;
    }

    /// <summary>The template's own annex clauses, then the requester's, each once and in order. Only approved clauses may travel.</summary>
    private async Task<List<Clause>> LoadAnnexClausesAsync(PdfTemplate template, List<Guid>? requested, CancellationToken ct)
    {
        var fixedIds = template.AnnexClauseIds;
        var requestedIds = (requested ?? []).Where(id => !fixedIds.Contains(id)).Distinct().ToList();
        var ids = fixedIds.Concat(requestedIds).ToList();
        if (ids.Count == 0) return [];

        var found = await db.Clauses.Where(c => ids.Contains(c.Id)).ToListAsync(ct);

        // A clause the template always carries stays even if it was deprecated since; one the requester picks must be approved.
        var unusable = requestedIds.Where(id => found.FirstOrDefault(c => c.Id == id) is not { Status: ClauseStatus.Approved }).ToList();
        if (unusable.Count > 0)
            throw new Common.Exceptions.ValidationException("Only approved clauses from the library can be added to a contract.");

        return [.. ids.Select(id => found.FirstOrDefault(c => c.Id == id)).OfType<Clause>()];
    }

    private async Task<List<(string Name, string Email, string? Company)>> ResolveApproversAsync(
        PdfTemplate template,
        List<PdfTemplateField> approverSigFields,
        List<PresetAssignment?> presetFor,
        List<ApproverSelection> supplied,
        CancellationToken cancellationToken)
    {
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
