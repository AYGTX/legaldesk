using SignFlow.Api.Filters;
using SignFlow.Application.Common.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Templates;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// The life of a template version (R-M6-5): draft → legal review → approved → retired. Only approved versions make
/// contracts; an approved version is changed by drafting its next one. {kind} is html or pdf.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/template-versions/{kind}/{id:guid}")]
[ApiController]
[Authorize]
public class TemplateVersionsController(
    ICommandHandler<SubmitTemplateCommand, TemplateVersionDto> submitHandler,
    ICommandHandler<ApproveTemplateCommand, TemplateVersionDto> approveHandler,
    ICommandHandler<RetireTemplateCommand, TemplateVersionDto> retireHandler,
    ICommandHandler<DraftNextTemplateVersionCommand, TemplateVersionDto> nextHandler) : ControllerBase
{
    /// <summary>Send a draft to legal review</summary>
    [HttpPost("submit")]
    [RequirePermission(Permissions.ManageTemplates)]
    [ProducesResponseType(typeof(TemplateVersionDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<TemplateVersionDto>> Submit([FromRoute] TemplateKind kind, [FromRoute] Guid id, CancellationToken ct)
        => Ok(await submitHandler.HandleAsync(new SubmitTemplateCommand(kind, id), ct));

    /// <summary>Approve a version in legal review: Harvey reviews it first, and an open critical finding refuses it</summary>
    [HttpPost("approve")]
    [RequirePermission(Permissions.ApproveTemplates)]
    [ProducesResponseType(typeof(TemplateVersionDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public async Task<ActionResult<TemplateVersionDto>> Approve([FromRoute] TemplateKind kind, [FromRoute] Guid id, CancellationToken ct)
        => Ok(await approveHandler.HandleAsync(new ApproveTemplateCommand(kind, id), ct));

    /// <summary>Retire a version: no new contract is made from it</summary>
    [HttpPost("retire")]
    [RequirePermission(Permissions.ApproveTemplates)]
    [ProducesResponseType(typeof(TemplateVersionDto), 200)]
    public async Task<ActionResult<TemplateVersionDto>> Retire([FromRoute] TemplateKind kind, [FromRoute] Guid id, CancellationToken ct)
        => Ok(await retireHandler.HandleAsync(new RetireTemplateCommand(kind, id), ct));

    /// <summary>Draft the next version of an approved template (or open the one already drafted)</summary>
    [HttpPost("next")]
    [RequirePermission(Permissions.ManageTemplates)]
    [ProducesResponseType(typeof(TemplateVersionDto), 200)]
    public async Task<ActionResult<TemplateVersionDto>> Next([FromRoute] TemplateKind kind, [FromRoute] Guid id, CancellationToken ct)
        => Ok(await nextHandler.HandleAsync(new DraftNextTemplateVersionCommand(kind, id), ct));
}
