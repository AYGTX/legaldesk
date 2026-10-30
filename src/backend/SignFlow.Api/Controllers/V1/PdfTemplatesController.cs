using SignFlow.Api.Filters;
using SignFlow.Application.Common.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Features.HtmlTemplates.Commands; // ApproverSelection
using SignFlow.Application.Features.PdfTemplates.Commands;
using SignFlow.Application.Features.PdfTemplates.Queries;
using SignFlow.Domain.Enums;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// PDF-template request flow: admins upload a base PDF once and map fields to page coordinates;
/// users create signing requests that reuse the stored PDF as their base document.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/pdf-templates")]
[ApiController]
[Authorize]
public class PdfTemplatesController(
    ICommandHandler<CreatePdfTemplateCommand, CreatePdfTemplateResult> createHandler,
    ICommandHandler<UpdatePdfTemplateCommand> updateHandler,
    ICommandHandler<DeletePdfTemplateCommand> deleteHandler,
    ICommandHandler<CreateRequestFromPdfTemplateCommand, Guid> submitHandler,
    IQueryHandler<GetPdfTemplatesQuery, PagedResult<PdfTemplateDto>> listHandler,
    IQueryHandler<GetPdfTemplateByIdQuery, PdfTemplateDetailDto> detailHandler,
    IStorageService storageService,
    ILegalCorpusClient legalCorpus) : ControllerBase
{
    /// <summary>List templates (admins/managers see all; others see active only)</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PdfTemplateDto>), 200)]
    public async Task<ActionResult<PagedResult<PdfTemplateDto>>> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? activeOnly = null,
        CancellationToken ct = default)
    {
        var result = await listHandler.HandleAsync(new GetPdfTemplatesQuery(pageNumber, pageSize, search, activeOnly), ct);
        return Ok(result);
    }

    /// <summary>Get template detail including field configuration</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PdfTemplateDetailDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<PdfTemplateDetailDto>> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await detailHandler.HandleAsync(new GetPdfTemplateByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>Stream the template's base PDF inline (for the field designer and request preview)</summary>
    [HttpGet("{id:guid}/stream")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Stream([FromRoute] Guid id, CancellationToken ct)
    {
        var template = await detailHandler.HandleAsync(new GetPdfTemplateByIdQuery(id), ct);
        var stream = await storageService.DownloadAsync(template.StoragePath, ct);
        // No filename → Content-Disposition: inline, letting the browser/PDF.js render it.
        return File(stream, "application/pdf", enableRangeProcessing: true);
    }

    /// <summary>
    /// Proposes fields for a contract PDF (text boxes, checkboxes, signatures) with a label, type, fill role and
    /// auto-fill for each, so a company can turn a finished contract into a template. Nothing is saved: the designer
    /// shows the proposal and the admin confirms or edits it. 503 when the legal-corpus service is not enabled.
    /// </summary>
    [HttpPost("detect-fields")]
    [RequirePermission(Permissions.ManageTemplates)]
    [RequestSizeLimit(25 * 1024 * 1024)]
    [ProducesResponseType(typeof(DetectedFormFields), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public async Task<ActionResult<DetectedFormFields>> DetectFields(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0 || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return Problem("Upload a PDF file.", statusCode: 400);

        await using var stream = file.OpenReadStream();
        return Ok(await legalCorpus.DetectFormFieldsAsync(stream, file.FileName, ct));
    }

    /// <summary>Upload the base PDF for a new template (admin/manager only). Fields are configured afterwards.</summary>
    [HttpPost]
    [RequirePermission(Permissions.ManageTemplates)]
    [RequestSizeLimit(51 * 1024 * 1024)] // 50 MB file + form overhead (R-M2-1)
    [ProducesResponseType(typeof(CreatePdfTemplateResult), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<CreatePdfTemplateResult>> Create([FromForm] CreatePdfTemplateRequest request, CancellationToken ct)
    {
        if (request.File.Length == 0)
            return BadRequest(new ProblemDetails { Title = "Invalid file", Detail = "File is empty." });

        var result = await createHandler.HandleAsync(
            new CreatePdfTemplateCommand(
                request.File.OpenReadStream(),
                request.File.FileName,
                request.File.Length,
                request.File.ContentType,
                request.Name,
                request.RequestTypeId,
                request.Description), ct);

        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>Update metadata/workflow config and replace the field set (admin/manager only)</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.ManageTemplates)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdatePdfTemplateRequest request, CancellationToken ct)
    {
        await updateHandler.HandleAsync(
            new UpdatePdfTemplateCommand(
                id,
                request.Name,
                request.Description,
                request.DefaultApproverId,
                request.IsActive,
                request.Fields.Select(f => new PdfTemplateFieldInput(
                    f.FieldKey, f.Label, f.Type, f.FillRole, f.Page, f.X, f.Y, f.Width, f.Height,
                    f.IsRequired, f.Order, f.AutoFillFrom, f.DefaultValue, f.OptionsJson,
                    f.PreAssignedApproverId, f.WorkflowStage)).ToList(),
                request.WorkflowType,
                request.HideRecipientIdentities,
                request.RequestTypeId,
                request.WorkflowPresetId,
                request.AnnexClauseIds), ct);
        return NoContent();
    }

    /// <summary>Edit the text of a template's PDF in the browser (admin/manager only). Requests already created keep their copy.</summary>
    [HttpPost("{id:guid}/edit-pdf")]
    [RequirePermission(Permissions.ManageTemplates)]
    [ProducesResponseType(typeof(EditPdfTemplateResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<EditPdfTemplateResult>> EditPdf(
        [FromRoute] Guid id,
        [FromBody] EditPdfRequest request,
        [FromServices] ICommandHandler<EditPdfTemplateCommand, EditPdfTemplateResult> editHandler,
        CancellationToken ct)
        => Ok(await editHandler.HandleAsync(new EditPdfTemplateCommand(id, request.Edits), ct));

    /// <summary>Soft-delete a template (admin/manager only)</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.ManageTemplates)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        await deleteHandler.HandleAsync(new DeletePdfTemplateCommand(id), ct);
        return NoContent();
    }

    /// <summary>Create a signing request from a template (any authenticated user)</summary>
    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType(typeof(SubmitFromPdfTemplateResponse), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<SubmitFromPdfTemplateResponse>> Submit(
        [FromRoute] Guid id, [FromBody] SubmitFromPdfTemplateRequest request, CancellationToken ct)
    {
        var documentId = await submitHandler.HandleAsync(
            new CreateRequestFromPdfTemplateCommand(
                id,
                request.FilledValues,
                request.Approvers?.Select(a => new ApproverSelection(a.UserId, a.ExternalName, a.ExternalEmail)).ToList(),
                request.Title,
                request.Message,
                request.ClauseIds,
                request.StartImmediately), ct);
        return CreatedAtAction(nameof(GetById), new { id }, new SubmitFromPdfTemplateResponse(documentId));
    }
}

// ---- Request / Response DTOs ----

public record CreatePdfTemplateRequest(IFormFile File, string Name, Guid RequestTypeId, string? Description = null);

public record PdfTemplateFieldRequest(
    string FieldKey,
    string Label,
    PdfFieldType Type,
    PdfFieldFillRole FillRole,
    int Page,
    float X,
    float Y,
    float Width,
    float Height,
    bool IsRequired,
    int Order,
    string? AutoFillFrom = null,
    string? DefaultValue = null,
    string? OptionsJson = null,
    Guid? PreAssignedApproverId = null,
    int WorkflowStage = 1);

public record UpdatePdfTemplateRequest(
    string Name,
    string? Description,
    Guid? DefaultApproverId,
    bool IsActive,
    List<PdfTemplateFieldRequest> Fields,
    WorkflowType WorkflowType = WorkflowType.Sequential,
    bool HideRecipientIdentities = false,
    Guid? RequestTypeId = null,
    Guid? WorkflowPresetId = null,
    List<Guid>? AnnexClauseIds = null);

// ApproverSelectionRequest is shared with HtmlTemplatesController (same namespace).

public record SubmitFromPdfTemplateRequest(
    Dictionary<string, string> FilledValues,
    List<ApproverSelectionRequest>? Approvers = null,
    string? Title = null,
    string? Message = null,
    List<Guid>? ClauseIds = null,
    bool StartImmediately = true);

public record SubmitFromPdfTemplateResponse(Guid DocumentId);
