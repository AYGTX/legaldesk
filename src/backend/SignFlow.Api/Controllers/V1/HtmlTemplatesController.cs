using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Features.HtmlTemplates.Commands;
using SignFlow.Application.Features.HtmlTemplates.Queries;

namespace SignFlow.Api.Controllers.V1;

/// <summary>HTML template management and request submission</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/html-templates")]
[ApiController]
[Authorize]
public class HtmlTemplatesController(
    ICommandHandler<CreateHtmlTemplateCommand, Guid> createHandler,
    ICommandHandler<UpdateHtmlTemplateCommand> updateHandler,
    ICommandHandler<DeleteHtmlTemplateCommand> deleteHandler,
    ICommandHandler<CreateRequestFromHtmlTemplateCommand, Guid> submitHandler,
    ICommandHandler<GenerateHtmlTemplateCommand, GenerateHtmlTemplateResult> generateHandler,
    ICommandHandler<ReviewHtmlTemplateCommand, ReviewHtmlTemplateResult> reviewHandler,
    IQueryHandler<GetHtmlTemplatesQuery, PagedResult<HtmlTemplateDto>> listHandler,
    IQueryHandler<GetHtmlTemplateByIdQuery, HtmlTemplateDetailDto> detailHandler) : ControllerBase
{
    /// <summary>List templates (admin/manager see all; others see active only)</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<HtmlTemplateDto>), 200)]
    public async Task<ActionResult<PagedResult<HtmlTemplateDto>>> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? activeOnly = null,
        CancellationToken ct = default)
    {
        var result = await listHandler.HandleAsync(new GetHtmlTemplatesQuery(pageNumber, pageSize, search, activeOnly), ct);
        return Ok(result);
    }

    /// <summary>Get template detail including HTML content and fields</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(HtmlTemplateDetailDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<HtmlTemplateDetailDto>> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await detailHandler.HandleAsync(new GetHtmlTemplateByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>Create a new HTML template (admin/manager only)</summary>
    [HttpPost]
    [Authorize(Roles = "Administrator,Manager")]
    [ProducesResponseType(typeof(CreateHtmlTemplateResponse), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<CreateHtmlTemplateResponse>> Create(
        [FromBody] CreateHtmlTemplateRequest request, CancellationToken ct)
    {
        var id = await createHandler.HandleAsync(
            new CreateHtmlTemplateCommand(
                request.Name,
                request.HtmlContent,
                request.Description,
                request.DefaultApproverId,
                request.Fields?.Select(f => new HtmlTemplateFieldInput(
                    f.FieldKey, f.Label, f.Type, f.Role, f.IsRequired, f.Order, f.AutoFillFrom, f.OptionsJson,
                    f.PreAssignedApproverId, f.WorkflowStage, f.DefaultValue)).ToList(),
                request.WorkflowType,
                request.HideRecipientIdentities,
                request.RequestTypeId,
                request.WorkflowPresetId), ct);
        return CreatedAtAction(nameof(GetById), new { id }, new CreateHtmlTemplateResponse(id));
    }

    /// <summary>Update an existing template (admin/manager only)</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Administrator,Manager")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateHtmlTemplateRequest request, CancellationToken ct)
    {
        await updateHandler.HandleAsync(
            new UpdateHtmlTemplateCommand(
                id,
                request.Name,
                request.HtmlContent,
                request.Description,
                request.DefaultApproverId,
                request.IsActive,
                request.Fields.Select(f => new HtmlTemplateFieldInput(
                    f.FieldKey, f.Label, f.Type, f.Role, f.IsRequired, f.Order, f.AutoFillFrom, f.OptionsJson,
                    f.PreAssignedApproverId, f.WorkflowStage, f.DefaultValue)).ToList(),
                request.WorkflowType,
                request.HideRecipientIdentities,
                request.RequestTypeId,
                request.WorkflowPresetId), ct);
        return NoContent();
    }

    /// <summary>Soft-delete a template (admin/manager only)</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Administrator,Manager")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        await deleteHandler.HandleAsync(new DeleteHtmlTemplateCommand(id), ct);
        return NoContent();
    }

    /// <summary>Generate HTML content from plain text using AI (admin/manager only)</summary>
    [HttpPost("generate")]
    [Authorize(Roles = "Administrator,Manager")]
    [ProducesResponseType(typeof(GenerateHtmlTemplateResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<ActionResult<GenerateHtmlTemplateResult>> Generate(
        [FromBody] GenerateHtmlTemplateRequest request, CancellationToken ct)
    {
        var result = await generateHandler.HandleAsync(new GenerateHtmlTemplateCommand(request.UserText), ct);
        return Ok(result);
    }

    /// <summary>
    /// Reviews the template as written (even unsaved) with the legal review: findings, each tied to a clause.
    /// 409 when the review could not run, rather than an empty result that reads as "no problems".
    /// </summary>
    [HttpPost("review")]
    [Authorize(Roles = "Administrator,Manager")]
    [ProducesResponseType(typeof(ReviewHtmlTemplateResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<ReviewHtmlTemplateResult>> Review(
        [FromBody] ReviewHtmlTemplateRequest request, CancellationToken ct)
    {
        var result = await reviewHandler.HandleAsync(
            new ReviewHtmlTemplateCommand(request.HtmlContent, request.Fields ?? []), ct);
        return Ok(result);
    }

    /// <summary>Submit a request from a template (any authenticated user)</summary>
    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType(typeof(SubmitFromTemplateResponse), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<SubmitFromTemplateResponse>> Submit(
        [FromRoute] Guid id, [FromBody] SubmitFromTemplateRequest request, CancellationToken ct)
    {
        var documentId = await submitHandler.HandleAsync(
            new CreateRequestFromHtmlTemplateCommand(
                id,
                request.FilledValues,
                request.Approvers?.Select(a => new ApproverSelection(a.UserId, a.ExternalName, a.ExternalEmail)).ToList(),
                request.Title,
                request.Message), ct);
        return CreatedAtAction(nameof(GetById), new { id }, new SubmitFromTemplateResponse(documentId));
    }
}

// ---- Request / Response DTOs ----

public record HtmlTemplateFieldRequest(
    string FieldKey,
    string Label,
    SignFlow.Domain.Enums.HtmlTemplateFieldType Type,
    SignFlow.Domain.Enums.HtmlTemplateFieldRole Role,
    bool IsRequired,
    int Order,
    string? AutoFillFrom = null,
    string? OptionsJson = null,
    Guid? PreAssignedApproverId = null,
    int WorkflowStage = 1,
    string? DefaultValue = null);

public record CreateHtmlTemplateRequest(
    string Name,
    string HtmlContent,
    string? Description = null,
    Guid? DefaultApproverId = null,
    List<HtmlTemplateFieldRequest>? Fields = null,
    SignFlow.Domain.Enums.WorkflowType WorkflowType = SignFlow.Domain.Enums.WorkflowType.Sequential,
    bool HideRecipientIdentities = false,
    Guid? RequestTypeId = null,
    Guid? WorkflowPresetId = null);

public record UpdateHtmlTemplateRequest(
    string Name,
    string HtmlContent,
    string? Description,
    Guid? DefaultApproverId,
    bool IsActive,
    List<HtmlTemplateFieldRequest> Fields,
    SignFlow.Domain.Enums.WorkflowType WorkflowType = SignFlow.Domain.Enums.WorkflowType.Sequential,
    bool HideRecipientIdentities = false,
    Guid? RequestTypeId = null,
    Guid? WorkflowPresetId = null);

public record ApproverSelectionRequest(Guid? UserId = null, string? ExternalName = null, string? ExternalEmail = null);

public record SubmitFromTemplateRequest(
    Dictionary<string, string> FilledValues,
    List<ApproverSelectionRequest>? Approvers = null,   // one per Approver Signature field needing a choice
    string? Title = null,
    string? Message = null);

public record CreateHtmlTemplateResponse(Guid Id);
public record GenerateHtmlTemplateRequest(string UserText);

public record ReviewHtmlTemplateRequest(string HtmlContent, List<ReviewTemplateField>? Fields);
public record SubmitFromTemplateResponse(Guid DocumentId);
