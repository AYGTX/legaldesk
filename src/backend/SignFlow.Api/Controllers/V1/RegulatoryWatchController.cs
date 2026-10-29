using SignFlow.Api.Filters;
using SignFlow.Application.Common.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Services;
using SignFlow.Application.Features.RegulatoryWatch.Commands;
using SignFlow.Application.Features.RegulatoryWatch.Queries;
using SignFlow.Domain.Enums;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// The regulatory watch registry — a legal-team-curated list of tracked regulatory texts (TN/EU/
/// sectoral). Curation is restricted to administrators and managers; publishing a text is an
/// administrator-only action and triggers a regulatory alert on contracts already in force.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/regulatory-texts")]
[ApiController]
[Authorize]
public class RegulatoryWatchController(
    IQueryHandler<GetRegulatoryTextsQuery, List<RegulatoryTextDto>> getAllHandler,
    IQueryHandler<GetPublishedRegulatoryTextsQuery, List<RegulatoryTextDto>> getPublishedHandler,
    ICommandHandler<CreateRegulatoryTextCommand, Guid> createHandler,
    ICommandHandler<UpdateRegulatoryTextCommand> updateHandler,
    ICommandHandler<SetRegulatoryTextStatusCommand> setStatusHandler,
    ICommandHandler<DeleteRegulatoryTextCommand> deleteHandler,
    IQueryHandler<PreviewRegulatoryArticlesQuery, ArticleSplit> previewHandler,
    IQueryHandler<GetRegulatoryArticlesQuery, List<RegulatoryArticleDto>> articlesHandler,
    ICommandHandler<SaveRegulatoryArticlesCommand> saveArticlesHandler,
    ICommandHandler<VerifyRegulatoryTextCommand> verifyHandler) : ControllerBase
{
    /// <summary>List every tracked regulatory text, including drafts (administrators and managers)</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<RegulatoryTextDto>), 200)]
    public async Task<ActionResult<List<RegulatoryTextDto>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] RegulatoryCategory? category = null,
        [FromQuery] RegulatoryJurisdiction? jurisdiction = null,
        [FromQuery] string? language = null,
        [FromQuery] RegulatoryTextStatus? status = null,
        CancellationToken ct = default)
        => Ok(await getAllHandler.HandleAsync(new GetRegulatoryTextsQuery(search, category, jurisdiction, language, status), ct));

    /// <summary>List published regulatory texts (any authenticated user)</summary>
    [HttpGet("published")]
    [ProducesResponseType(typeof(List<RegulatoryTextDto>), 200)]
    public async Task<ActionResult<List<RegulatoryTextDto>>> GetPublished(
        [FromQuery] string? search = null,
        [FromQuery] RegulatoryCategory? category = null,
        [FromQuery] RegulatoryJurisdiction? jurisdiction = null,
        [FromQuery] string? language = null,
        [FromQuery] Guid? requestTypeId = null,
        CancellationToken ct = default)
        => Ok(await getPublishedHandler.HandleAsync(new GetPublishedRegulatoryTextsQuery(search, category, jurisdiction, language, requestTypeId), ct));

    /// <summary>Add a regulatory text to the registry (created as a draft)</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateRegulatoryTextResponse), 201)]
    public async Task<ActionResult<CreateRegulatoryTextResponse>> Create([FromBody] CreateRegulatoryTextRequest request, CancellationToken ct)
    {
        var id = await createHandler.HandleAsync(
            new CreateRegulatoryTextCommand(request.Title, request.Category, request.Jurisdiction, request.Summary,
                request.Keywords, request.Language, request.Reference, request.EffectiveDate, request.RequestTypeId, request.SourceUrl), ct);
        return CreatedAtAction(nameof(GetAll), new { }, new CreateRegulatoryTextResponse(id));
    }

    /// <summary>Update a regulatory text's summary or metadata</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateRegulatoryTextRequest request, CancellationToken ct)
    {
        await updateHandler.HandleAsync(
            new UpdateRegulatoryTextCommand(id, request.Title, request.Category, request.Jurisdiction, request.Summary,
                request.Keywords, request.Language, request.Reference, request.EffectiveDate, request.RequestTypeId, request.SourceUrl), ct);
        return NoContent();
    }

    /// <summary>Show how pasted law text would be cut into articles. Nothing is saved.</summary>
    [HttpPost("articles/preview")]
    [ProducesResponseType(typeof(ArticleSplit), 200)]
    public async Task<ActionResult<ArticleSplit>> PreviewArticles([FromBody] PreviewArticlesRequest request, CancellationToken ct)
        => Ok(await previewHandler.HandleAsync(new PreviewRegulatoryArticlesQuery(request.RawText), ct));

    /// <summary>The articles of a regulatory text, in reading order</summary>
    [HttpGet("{id:guid}/articles")]
    [ProducesResponseType(typeof(List<RegulatoryArticleDto>), 200)]
    public async Task<ActionResult<List<RegulatoryArticleDto>>> GetArticles([FromRoute] Guid id, CancellationToken ct)
        => Ok(await articlesHandler.HandleAsync(new GetRegulatoryArticlesQuery(id), ct));

    /// <summary>Replace the articles of a regulatory text. Withdraws any earlier verification.</summary>
    [HttpPut("{id:guid}/articles")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> SaveArticles([FromRoute] Guid id, [FromBody] SaveArticlesRequest request, CancellationToken ct)
    {
        await saveArticlesHandler.HandleAsync(new SaveRegulatoryArticlesCommand(id, request.Articles), ct);
        return NoContent();
    }

    /// <summary>Attest that the stored articles are the official text (administrators and managers)</summary>
    [HttpPost("{id:guid}/verify")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Verify([FromRoute] Guid id, CancellationToken ct)
    {
        await verifyHandler.HandleAsync(new VerifyRegulatoryTextCommand(id), ct);
        return NoContent();
    }

    /// <summary>Publish or archive a regulatory text (administrators only). Publishing alerts owners of contracts in force.</summary>
    [HttpPatch("{id:guid}/status")]
    [RequirePermission(Permissions.PublishRegulatoryTexts)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> SetStatus([FromRoute] Guid id, [FromBody] SetRegulatoryTextStatusRequest request, CancellationToken ct)
    {
        await setStatusHandler.HandleAsync(new SetRegulatoryTextStatusCommand(id, request.Status), ct);
        return NoContent();
    }

    /// <summary>Delete a regulatory text that has never been published (administrators only)</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.PublishRegulatoryTexts)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        await deleteHandler.HandleAsync(new DeleteRegulatoryTextCommand(id), ct);
        return NoContent();
    }
}

/// <summary>Payload to add a regulatory text to the registry.</summary>
public record CreateRegulatoryTextRequest(
    string Title,
    RegulatoryCategory Category,
    RegulatoryJurisdiction Jurisdiction,
    string Summary,
    string Keywords,
    string Language,
    string? Reference,
    DateTime? EffectiveDate,
    Guid? RequestTypeId,
    string? SourceUrl = null);

/// <summary>Payload to update an existing regulatory text.</summary>
public record UpdateRegulatoryTextRequest(
    string Title,
    RegulatoryCategory Category,
    RegulatoryJurisdiction Jurisdiction,
    string Summary,
    string Keywords,
    string Language,
    string? Reference,
    DateTime? EffectiveDate,
    Guid? RequestTypeId,
    string? SourceUrl = null);

public record PreviewArticlesRequest(string RawText);
public record SaveArticlesRequest(List<RegulatoryArticleInput> Articles);

/// <summary>Payload to move a regulatory text through its publication lifecycle.</summary>
public record SetRegulatoryTextStatusRequest(RegulatoryTextStatus Status);

/// <summary>Identifier of the newly created regulatory text.</summary>
public record CreateRegulatoryTextResponse(Guid Id);
