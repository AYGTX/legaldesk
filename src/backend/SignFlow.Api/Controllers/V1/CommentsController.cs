using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Comments;
using SignFlow.Application.Features.Comments.Commands;
using SignFlow.Application.Features.Comments.Queries;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Document comments and threaded discussions (collaboration layer)</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[ApiController]
[Authorize]
public class CommentsController(
    IQueryHandler<GetDocumentCommentsQuery, List<CommentDto>> listHandler,
    ICommandHandler<AddCommentCommand, CommentDto> addHandler,
    ICommandHandler<ResolveCommentCommand> resolveHandler,
    ICommandHandler<DeleteCommentCommand> deleteHandler) : ControllerBase
{
    /// <summary>List all comments on a document (owner, admin, or recipient)</summary>
    [HttpGet("documents/{documentId:guid}/comments")]
    [ProducesResponseType(typeof(List<CommentDto>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<List<CommentDto>>> GetComments([FromRoute] Guid documentId, CancellationToken ct)
    {
        var result = await listHandler.HandleAsync(new GetDocumentCommentsQuery(documentId), ct);
        return Ok(result);
    }

    /// <summary>The people a comment on this document can mention: members of the organisation and its participants</summary>
    [HttpGet("documents/{documentId:guid}/mentionables")]
    [ProducesResponseType(typeof(List<MentionableDto>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<ActionResult<List<MentionableDto>>> GetMentionables(
        [FromRoute] Guid documentId,
        [FromServices] IQueryHandler<GetMentionablesQuery, List<MentionableDto>> mentionablesHandler,
        CancellationToken ct)
    {
        return Ok(await mentionablesHandler.HandleAsync(new GetMentionablesQuery(documentId), ct));
    }

    /// <summary>Add a comment or a reply to a document</summary>
    [HttpPost("documents/{documentId:guid}/comments")]
    [ProducesResponseType(typeof(CommentDto), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<CommentDto>> AddComment(
        [FromRoute] Guid documentId,
        [FromBody] AddCommentRequest request,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await addHandler.HandleAsync(
            new AddCommentCommand(documentId, request.Content, request.ParentCommentId, request.Page, request.X, request.Y, ip,
                request.Visibility ?? SignFlow.Domain.Enums.CommentVisibility.Internal, request.MentionIds), ct);
        return StatusCode(201, result);
    }

    /// <summary>Resolve or reopen a comment thread (owner, admin, or comment author)</summary>
    [HttpPut("comments/{id:guid}/resolve")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Resolve([FromRoute] Guid id, [FromBody] ResolveCommentRequest request, CancellationToken ct)
    {
        await resolveHandler.HandleAsync(new ResolveCommentCommand(id, request.Resolved), ct);
        return NoContent();
    }

    /// <summary>Delete a comment and, for top-level threads, its replies (owner, admin, or author)</summary>
    [HttpDelete("comments/{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        await deleteHandler.HandleAsync(new DeleteCommentCommand(id), ct);
        return NoContent();
    }
}

/// <summary>Request body for adding a comment; position fields are optional page-anchoring.</summary>
public record AddCommentRequest(string Content, Guid? ParentCommentId, int? Page, float? X, float? Y,
    SignFlow.Domain.Enums.CommentVisibility? Visibility = null, List<Guid>? MentionIds = null);

/// <summary>Request body for resolving or reopening a comment thread.</summary>
public record ResolveCommentRequest(bool Resolved);
