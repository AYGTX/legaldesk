using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Checklist.Commands;
using SignFlow.Application.Features.Comments;
using SignFlow.Application.Features.Comments.Commands;
using SignFlow.Application.Features.Comments.Queries;
using SignFlow.Application.Features.Documents.Queries;
using SignFlow.Application.Features.Signing;
using SignFlow.Application.Features.Signing.Commands;
using SignFlow.Application.Features.Signing.Queries;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// The participant's side of a contract (M5), no sign-in. The e-mailed link shows what is asked and sends a 6-digit code;
/// the code opens a signing session (header X-Signing-Session) and only a session reads the contract or acts. Every
/// endpoint goes through <see cref="ISigningAccess"/>; a refusal answers with a code the page explains (R-M5-2).
/// Signed-in participants open a session from their account instead and use "session" as the token.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sign")]
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimits.Signing)]
public class SigningController(
    ISigningAccess access,
    IQueryHandler<GetSigningSessionQuery, SigningSessionDto> sessionHandler,
    ICommandHandler<SignFieldCommand, SignFieldResult> signHandler,
    ICommandHandler<ApproveSigningCommand, ApproveSigningResult> approveHandler,
    ICommandHandler<RejectDocumentCommand> rejectHandler,
    ICommandHandler<AcknowledgeReviewCommand> acknowledgeHandler,
    IStorageService storageService) : ControllerBase
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string UserAgent => Request.Headers.UserAgent.ToString();

    private Task<SigningContext> Gate(string token, SigningNeed need, CancellationToken ct)
    {
        var session = Request.Headers[SigningAccess.SessionHeader].ToString();
        return access.ResolveAsync(token, string.IsNullOrWhiteSpace(session) ? null : session, need, ct);
    }

    private static SigningEvidence Evidence(SigningContext c) =>
        new(c.Session?.VerifiedEmail, c.Session?.Method, c.Session?.VerifiedAt);

    /// <summary>What the link is for: contract title, sender, participant, masked e-mail. Nothing of the contract.</summary>
    [HttpGet("{token}/intro")]
    [ProducesResponseType(typeof(SigningIntroDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 410)]
    public async Task<ActionResult<SigningIntroDto>> Intro([FromRoute] string token, CancellationToken ct)
        => Ok(await access.IntroAsync(token, ct));

    /// <summary>E-mail a 6-digit code to the participant (valid 10 minutes)</summary>
    [HttpPost("{token}/otp/send")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 429)]
    public async Task<IActionResult> SendCode([FromRoute] string token, CancellationToken ct)
    {
        await access.SendCodeAsync(token, ct);
        return NoContent();
    }

    /// <summary>Check the code; opens a 30-minute signing session</summary>
    [HttpPost("{token}/otp/verify")]
    [ProducesResponseType(typeof(OpenedSigningSession), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<OpenedSigningSession>> VerifyCode([FromRoute] string token, [FromBody] VerifyCodeRequest request, CancellationToken ct)
        => Ok(await access.VerifyCodeAsync(token, request.Code, Ip, ct));

    /// <summary>The signing session: the contract, the participant's fields and the circuit</summary>
    [HttpGet("{token}")]
    [ProducesResponseType(typeof(SigningSessionDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    public async Task<ActionResult<SigningSessionDto>> GetSession([FromRoute] string token, CancellationToken ct)
    {
        var context = await Gate(token, SigningNeed.View, ct);
        return Ok(await sessionHandler.HandleAsync(new GetSigningSessionQuery(context.Recipient.Id), ct));
    }

    /// <summary>The PDF being signed</summary>
    [HttpGet("{token}/document")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetDocument([FromRoute] string token, CancellationToken ct)
    {
        var context = await Gate(token, SigningNeed.View, ct);
        var session = await sessionHandler.HandleAsync(new GetSigningSessionQuery(context.Recipient.Id), ct);
        // A contract built from an HTML template has no file until it is signed; once signed, the final PDF is served.
        if (session.StoragePath.StartsWith("html-templates/"))
            return NotFound(new ProblemDetails { Detail = "PDF not yet available — document must be fully signed first." });
        var stream = await storageService.DownloadAsync(session.StoragePath, ct);
        return File(stream, "application/pdf", enableRangeProcessing: true);
    }

    /// <summary>Fill or sign one field</summary>
    [HttpPost("{token}/fields/{fieldId:guid}")]
    [ProducesResponseType(typeof(SignFieldResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<SignFieldResult>> SignField(
        [FromRoute] string token, [FromRoute] Guid fieldId, [FromBody] SignFieldRequest request, CancellationToken ct)
    {
        var context = await Gate(token, SigningNeed.Act, ct);
        return Ok(await signHandler.HandleAsync(new SignFieldCommand(
            context.Recipient.Id, fieldId, request.SignatureType, request.ImageBase64, request.TypedText, request.FontFamily,
            Ip, UserAgent, Evidence(context)), ct));
    }

    /// <summary>Confirm the contract and its review were read — required before signing, approving or reviewing</summary>
    [HttpPost("{token}/acknowledge-review")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> AcknowledgeReview([FromRoute] string token, CancellationToken ct)
    {
        var context = await Gate(token, SigningNeed.View, ct);
        await acknowledgeHandler.HandleAsync(new AcknowledgeReviewCommand(context.Recipient.Id, Ip, UserAgent), ct);
        return NoContent();
    }

    /// <summary>Approve, after every required field is filled: advances the circuit</summary>
    [HttpPost("{token}/approve")]
    [ProducesResponseType(typeof(ApproveSigningResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<ApproveSigningResult>> Approve([FromRoute] string token, CancellationToken ct)
    {
        var context = await Gate(token, SigningNeed.Act, ct);
        return Ok(await approveHandler.HandleAsync(new ApproveSigningCommand(context.Recipient.Id, Ip, UserAgent), ct));
    }

    /// <summary>A plain-language summary of the contract</summary>
    [HttpGet("{token}/summary")]
    [ProducesResponseType(typeof(AiDocumentSummaryDto), 200)]
    public async Task<ActionResult<AiDocumentSummaryDto>> GetSummary(
        [FromRoute] string token,
        [FromServices] IQueryHandler<GetDocumentSummaryQuery, AiDocumentSummaryDto> summaryHandler,
        CancellationToken ct)
    {
        var context = await Gate(token, SigningNeed.View, ct);
        return Ok(await summaryHandler.HandleAsync(new GetDocumentSummaryQuery(context.Document.Id), ct));
    }

    /// <summary>The contract's comments visible to participants</summary>
    [HttpGet("{token}/comments")]
    [ProducesResponseType(typeof(List<CommentDto>), 200)]
    public async Task<ActionResult<List<CommentDto>>> GetComments(
        [FromRoute] string token,
        [FromServices] IQueryHandler<GetSigningCommentsQuery, List<CommentDto>> commentsHandler,
        CancellationToken ct)
    {
        var context = await Gate(token, SigningNeed.View, ct);
        return Ok(await commentsHandler.HandleAsync(new GetSigningCommentsQuery(context.Recipient.Id), ct));
    }

    /// <summary>Add a comment or a reply as the participant</summary>
    [HttpPost("{token}/comments")]
    [ProducesResponseType(typeof(CommentDto), 201)]
    public async Task<ActionResult<CommentDto>> AddComment(
        [FromRoute] string token,
        [FromBody] AddSigningCommentRequest request,
        [FromServices] ICommandHandler<AddSigningCommentCommand, CommentDto> addCommentHandler,
        CancellationToken ct)
    {
        var context = await Gate(token, SigningNeed.View, ct);
        var result = await addCommentHandler.HandleAsync(new AddSigningCommentCommand(
            context.Recipient.Id, request.Content, request.ParentCommentId, request.Page, request.X, request.Y, Ip), ct);
        return StatusCode(201, result);
    }

    /// <summary>Decline the contract, with a reason</summary>
    [HttpPost("{token}/decline")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> Decline([FromRoute] string token, [FromBody] RejectRequest request, CancellationToken ct)
    {
        var context = await Gate(token, SigningNeed.Act, ct);
        await rejectHandler.HandleAsync(new RejectDocumentCommand(context.Recipient.Id, request.Reason), ct);
        return NoContent();
    }

    /// <summary>Ask the sender for changes — pauses the circuit instead of ending it</summary>
    [HttpPost("{token}/request-changes")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> RequestChanges(
        [FromRoute] string token,
        [FromBody] RequestChangesRequest request,
        [FromServices] ICommandHandler<RequestChangesCommand> requestChangesHandler,
        CancellationToken ct)
    {
        var context = await Gate(token, SigningNeed.Act, ct);
        await requestChangesHandler.HandleAsync(new RequestChangesCommand(context.Recipient.Id, request.Reason, Ip), ct);
        return NoContent();
    }

    /// <summary>Check or uncheck one of the participant's checklist items</summary>
    [HttpPost("{token}/checklist/{itemId:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ToggleChecklistItem(
        [FromRoute] string token,
        [FromRoute] Guid itemId,
        [FromBody] ToggleChecklistItemRequest request,
        [FromServices] ICommandHandler<ToggleChecklistItemCommand> toggleHandler,
        CancellationToken ct)
    {
        var context = await Gate(token, SigningNeed.Act, ct);
        await toggleHandler.HandleAsync(new ToggleChecklistItemCommand(context.Recipient.Id, itemId, request.Checked), ct);
        return NoContent();
    }
}

public record VerifyCodeRequest(string Code);
public record SignFieldRequest(SignFlow.Domain.Enums.SignatureType SignatureType, string? ImageBase64, string? TypedText, string? FontFamily);
public record RejectRequest(string Reason);
public record RequestChangesRequest(string Reason);
public record ToggleChecklistItemRequest(bool Checked);

/// <summary>Request body for adding a comment via a signing token; position fields are optional page-anchoring.</summary>
public record AddSigningCommentRequest(string Content, Guid? ParentCommentId, int? Page, float? X, float? Y);
