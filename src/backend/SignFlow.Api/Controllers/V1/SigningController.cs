using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Comments;
using SignFlow.Application.Features.Comments.Commands;
using SignFlow.Application.Features.Comments.Queries;
using SignFlow.Application.Features.Documents.Queries;
using SignFlow.Application.Features.Signing.Commands;
using SignFlow.Application.Features.Signing.Queries;

namespace SignFlow.Api.Controllers.V1;

/// <summary>External signing endpoints — no authentication required</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sign")]
[ApiController]
public class SigningController(
    IQueryHandler<GetSigningSessionQuery, SigningSessionDto> sessionHandler,
    ICommandHandler<SignFieldCommand, SignFieldResult> signHandler,
    ICommandHandler<ApproveSigningCommand, ApproveSigningResult> approveHandler,
    ICommandHandler<RejectDocumentCommand> rejectHandler,
    IStorageService storageService) : ControllerBase
{
    /// <summary>Get the signing session for a recipient</summary>
    [HttpGet("{token}")]
    [ProducesResponseType(typeof(SigningSessionDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<SigningSessionDto>> GetSession([FromRoute] string token, CancellationToken ct)
    {
        var result = await sessionHandler.HandleAsync(new GetSigningSessionQuery(token), ct);
        return Ok(result);
    }

    /// <summary>Stream the document PDF inline for the signing session (token-authenticated)</summary>
    [HttpGet("{token}/document")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetDocument([FromRoute] string token, CancellationToken ct)
    {
        var session = await sessionHandler.HandleAsync(new GetSigningSessionQuery(token), ct);
        // StoragePath is FinalStoragePath ?? StoragePath. For HTML template docs that haven't
        // been signed yet, StoragePath is the placeholder "html-templates/{id}" with no real file.
        // Once signed, FinalStoragePath (a real PDF) takes precedence and we serve it normally.
        if (session.StoragePath.StartsWith("html-templates/"))
            return NotFound(new ProblemDetails { Detail = "PDF not yet available — document must be fully signed first." });
        var stream = await storageService.DownloadAsync(session.StoragePath, ct);
        return File(stream, "application/pdf", enableRangeProcessing: true);
    }

    /// <summary>Sign a single field</summary>
    [HttpPost("{token}/fields/{fieldId:guid}/sign")]
    [ProducesResponseType(typeof(SignFieldResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<SignFieldResult>> SignField(
        [FromRoute] string token,
        [FromRoute] Guid fieldId,
        [FromBody] SignFieldRequest request,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = Request.Headers.UserAgent.ToString();

        var command = new SignFieldCommand(
            token, fieldId, request.SignatureType, request.ImageBase64, request.TypedText, request.FontFamily, ip, userAgent, request.Location);

        var result = await signHandler.HandleAsync(command, ct);
        return Ok(result);
    }

    /// <summary>Approve signing after reviewing all filled fields — stamps PDF and advances workflow</summary>
    [HttpPost("{token}/approve")]
    [ProducesResponseType(typeof(ApproveSigningResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<ApproveSigningResult>> Approve([FromRoute] string token, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = Request.Headers.UserAgent.ToString();
        var result = await approveHandler.HandleAsync(new ApproveSigningCommand(token, ip, userAgent), ct);
        return Ok(result);
    }

    /// <summary>Get an AI-generated summary of the document (token-authenticated, no login required)</summary>
    [HttpGet("{token}/summary")]
    [ProducesResponseType(typeof(AiDocumentSummaryDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<AiDocumentSummaryDto>> GetSummary(
        [FromRoute] string token,
        [FromServices] IQueryHandler<GetDocumentSummaryQuery, AiDocumentSummaryDto> summaryHandler,
        CancellationToken ct)
    {
        var session = await sessionHandler.HandleAsync(new GetSigningSessionQuery(token), ct);
        var result = await summaryHandler.HandleAsync(new GetDocumentSummaryQuery(session.DocumentId), ct);
        return Ok(result);
    }

    /// <summary>List the document's comments for this signing session (token-authenticated)</summary>
    [HttpGet("{token}/comments")]
    [ProducesResponseType(typeof(List<CommentDto>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<List<CommentDto>>> GetComments(
        [FromRoute] string token,
        [FromServices] IQueryHandler<GetSigningCommentsQuery, List<CommentDto>> commentsHandler,
        CancellationToken ct)
    {
        var result = await commentsHandler.HandleAsync(new GetSigningCommentsQuery(token), ct);
        return Ok(result);
    }

    /// <summary>Add a comment or a reply as the signing-session participant (token-authenticated)</summary>
    [HttpPost("{token}/comments")]
    [ProducesResponseType(typeof(CommentDto), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<CommentDto>> AddComment(
        [FromRoute] string token,
        [FromBody] AddSigningCommentRequest request,
        [FromServices] ICommandHandler<AddSigningCommentCommand, CommentDto> addCommentHandler,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await addCommentHandler.HandleAsync(
            new AddSigningCommentCommand(token, request.Content, request.ParentCommentId, request.Page, request.X, request.Y, ip), ct);
        return StatusCode(201, result);
    }

    /// <summary>Reject a document</summary>
    [HttpPost("{token}/reject")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Reject([FromRoute] string token, [FromBody] RejectRequest request, CancellationToken ct)
    {
        await rejectHandler.HandleAsync(new RejectDocumentCommand(token, request.Reason), ct);
        return NoContent();
    }
}

public record SignFieldRequest(SignFlow.Domain.Enums.SignatureType SignatureType, string? ImageBase64, string? TypedText, string? FontFamily, string? Location);
public record RejectRequest(string Reason);

/// <summary>Request body for adding a comment via a signing token; position fields are optional page-anchoring.</summary>
public record AddSigningCommentRequest(string Content, Guid? ParentCommentId, int? Page, float? X, float? Y);
