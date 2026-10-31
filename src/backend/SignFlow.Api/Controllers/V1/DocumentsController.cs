using SignFlow.Application.Features.Reports.Queries;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Features.Documents.Commands;
using SignFlow.Application.Features.Documents.Queries;
using SignFlow.Application.Features.Reviews;
using SignFlow.Application.Features.RequestTypes.Queries;
using SignFlow.Domain.Enums;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Document management endpoints</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[Authorize]
public class DocumentsController(
    ICommandHandler<UploadDocumentCommand, DocumentUploadResult> uploadHandler,
    ICommandHandler<SendDocumentCommand> sendHandler,
    ICommandHandler<DeleteDocumentCommand> deleteHandler,
    IQueryHandler<GetDocumentsQuery, PagedResult<DocumentSummaryDto>> listHandler,
    IQueryHandler<GetDocumentByIdQuery, DocumentDetailDto> getHandler,
    IQueryHandler<GetDashboardQuery, DashboardDto> dashboardHandler,
    IStorageService storageService) : ControllerBase
{
    /// <summary>Get dashboard statistics</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(DashboardDto), 200)]
    public async Task<ActionResult<DashboardDto>> GetDashboard(CancellationToken ct)
    {
        var result = await dashboardHandler.HandleAsync(new GetDashboardQuery(), ct);
        return Ok(result);
    }

    /// <summary>The contract schedule: every contract with its end date, notice deadline and stage, most urgent first
    /// (administrators see all contracts, others their own)</summary>
    [HttpGet("schedule")]
    [ProducesResponseType(typeof(List<ContractScheduleRowDto>), 200)]
    public async Task<ActionResult<List<ContractScheduleRowDto>>> GetSchedule(
        [FromServices] IQueryHandler<GetContractScheduleQuery, List<ContractScheduleRowDto>> scheduleHandler, CancellationToken ct) =>
        Ok(await scheduleHandler.HandleAsync(new GetContractScheduleQuery(), ct));

    /// <summary>Get SLA metrics for the current user's requests</summary>
    [HttpGet("sla-metrics")]
    [ProducesResponseType(typeof(SlaMetricsDto), 200)]
    public async Task<ActionResult<SlaMetricsDto>> GetSlaMetrics(
        [FromServices] IQueryHandler<GetSlaMetricsQuery, SlaMetricsDto> slaHandler, CancellationToken ct)
    {
        var result = await slaHandler.HandleAsync(new GetSlaMetricsQuery(), ct);
        return Ok(result);
    }

    /// <summary>Reopen an expired request (administrators only)</summary>
    [HttpPost("{id:guid}/reopen")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Reopen(
        [FromRoute] Guid id,
        [FromBody] ReopenDocumentRequest? request,
        [FromServices] ICommandHandler<ReopenDocumentCommand> reopenHandler,
        CancellationToken ct)
    {
        await reopenHandler.HandleAsync(new ReopenDocumentCommand(id, request?.SlaHours), ct);
        return NoContent();
    }

    /// <summary>Confirm or override the AI-suggested obligation (renewal/expiry) date</summary>
    [HttpPatch("{id:guid}/obligation")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> UpdateObligation(
        [FromRoute] Guid id,
        [FromBody] UpdateObligationRequest request,
        [FromServices] ICommandHandler<UpdateObligationCommand> handler,
        CancellationToken ct)
    {
        await handler.HandleAsync(new UpdateObligationCommand(id, request.DueDate, request.ObligationType), ct);
        return NoContent();
    }

    /// <summary>Record the negotiated contract term that drives the lifecycle alerts</summary>
    [HttpPatch("{id:guid}/term")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> UpdateContractTerm(
        [FromRoute] Guid id,
        [FromBody] UpdateContractTermRequest request,
        [FromServices] ICommandHandler<UpdateContractTermCommand> handler,
        CancellationToken ct)
    {
        await handler.HandleAsync(
            new UpdateContractTermCommand(
                id, request.StartDate, request.EndDate, request.AutoRenews, request.NoticePeriodDays),
            ct);
        return NoContent();
    }

    /// <summary>List documents with pagination, filtering and sorting</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<DocumentSummaryDto>), 200)]
    public async Task<ActionResult<PagedResult<DocumentSummaryDto>>> GetDocuments(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] DocumentStatus? status = null,
        [FromQuery] string? sortBy = "createdAt",
        [FromQuery] bool descending = true,
        [FromQuery] Guid? requestTypeId = null,
        [FromQuery] string? statuses = null,
        CancellationToken ct = default)
    {
        // Comma-separated statuses ("Draft,Sent") for the multi-select filter; unknown names are ignored.
        var statusList = (statuses ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Enum.TryParse<DocumentStatus>(s, true, out var v) ? (DocumentStatus?)v : null)
            .Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var query = new GetDocumentsQuery(pageNumber, Math.Min(pageSize, 100), search, status, sortBy, descending, requestTypeId, statusList);
        var result = await listHandler.HandleAsync(query, ct);
        return Ok(result);
    }

    /// <summary>Get document details by ID</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DocumentDetailDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<DocumentDetailDto>> GetDocument([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await getHandler.HandleAsync(new GetDocumentByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>Upload a new PDF document</summary>
    [HttpPost]
    [RequestSizeLimit(51 * 1024 * 1024)] // 50 MB file + form overhead (R-M2-1)
    [ProducesResponseType(typeof(DocumentUploadResult), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<DocumentUploadResult>> Upload([FromForm] UploadDocumentRequest request, CancellationToken ct)
    {
        if (request.File.Length == 0)
            return BadRequest(new ProblemDetails { Title = "Invalid file", Detail = "File is empty." });

        var command = new UploadDocumentCommand(
            request.File.OpenReadStream(),
            request.File.FileName,
            request.File.Length,
            request.File.ContentType,
            request.Title,
            request.Description,
            request.Subject,
            request.Message,
            request.ExpirationDays,
            request.RequestTypeId);

        var result = await uploadHandler.HandleAsync(command, ct);
        return CreatedAtAction(nameof(GetDocument), new { id = result.DocumentId }, result);
    }

    /// <summary>Upload a revised PDF after changes were requested — voids field completion and returns the document to Draft for re-send</summary>
    [HttpPost("{id:guid}/revision")]
    [RequestSizeLimit(51 * 1024 * 1024)] // 50 MB file + form overhead (R-M2-1)
    [ProducesResponseType(typeof(UploadRevisionResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<UploadRevisionResult>> UploadRevision(
        [FromRoute] Guid id,
        [FromForm] UploadRevisionRequest request,
        [FromServices] ICommandHandler<UploadRevisionCommand, UploadRevisionResult> revisionHandler,
        CancellationToken ct)
    {
        if (request.File.Length == 0)
            return BadRequest(new ProblemDetails { Title = "Invalid file", Detail = "File is empty." });

        var command = new UploadRevisionCommand(
            id,
            request.File.OpenReadStream(),
            request.File.FileName,
            request.File.Length,
            request.File.ContentType,
            request.ChangeNote);

        var result = await revisionHandler.HandleAsync(command, ct);
        return Ok(result);
    }

    /// <summary>The form an owner fills again after changes were requested on a template contract: the changeable answers, their current values, and what the reviewer asked for</summary>
    [HttpGet("{id:guid}/revision-form")]
    [ProducesResponseType(typeof(TemplateRevisionFormDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<TemplateRevisionFormDto>> GetRevisionForm(
        [FromRoute] Guid id,
        [FromServices] IQueryHandler<GetTemplateRevisionFormQuery, TemplateRevisionFormDto> formHandler,
        CancellationToken ct)
    {
        var result = await formHandler.HandleAsync(new GetTemplateRevisionFormQuery(id), ct);
        return Ok(result);
    }

    /// <summary>Revise a template contract by changing its answers — keeps the old version, creates the next one and sends the circuit again</summary>
    [HttpPost("{id:guid}/revise-answers")]
    [ProducesResponseType(typeof(UploadRevisionResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<UploadRevisionResult>> ReviseAnswers(
        [FromRoute] Guid id,
        [FromBody] ReviseAnswersRequest request,
        [FromServices] ICommandHandler<ReviseFromTemplateCommand, UploadRevisionResult> reviseHandler,
        CancellationToken ct)
    {
        var result = await reviseHandler.HandleAsync(new ReviseFromTemplateCommand(id, request.FilledValues ?? [], request.ChangeNote), ct);
        return Ok(result);
    }

    /// <summary>Edit the text of a PDF contract in the browser — applied to the stored file and saved as a new version</summary>
    [HttpPost("{id:guid}/edit-pdf")]
    [ProducesResponseType(typeof(UploadRevisionResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<UploadRevisionResult>> EditPdf(
        [FromRoute] Guid id,
        [FromBody] EditPdfRequest request,
        [FromServices] ICommandHandler<EditDocumentPdfCommand, UploadRevisionResult> editHandler,
        CancellationToken ct)
    {
        var result = await editHandler.HandleAsync(new EditDocumentPdfCommand(id, request.Edits, request.ChangeNote), ct);
        return Ok(result);
    }

    /// <summary>Send document for signing</summary>
    [HttpPost("{id:guid}/send")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> Send(
        [FromRoute] Guid id,
        [FromBody] SendDocumentRequest? request,
        CancellationToken ct)
    {
        await sendHandler.HandleAsync(new SendDocumentCommand(id, request?.Reason), ct);
        return NoContent();
    }

    /// <summary>Propose the contract's fields from its PDF (owner); nothing is saved (R-M3-7)</summary>
    [HttpPost("{id:guid}/detect-fields")]
    [ProducesResponseType(typeof(DetectedFormFields), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public async Task<ActionResult<DetectedFormFields>> DetectFields(
        [FromRoute] Guid id,
        [FromServices] IQueryHandler<SignFlow.Application.Features.SignatureFields.DetectContractFieldsQuery, DetectedFormFields> handler,
        CancellationToken ct)
        => Ok(await handler.HandleAsync(new SignFlow.Application.Features.SignatureFields.DetectContractFieldsQuery(id), ct));

    /// <summary>Administrators: fields outside their page after the move to points (R-M3-8)</summary>
    [HttpGet("field-layout-report")]
    [ProducesResponseType(typeof(List<SignFlow.Application.Features.SignatureFields.FieldOutsidePageDto>), 200)]
    public async Task<ActionResult<List<SignFlow.Application.Features.SignatureFields.FieldOutsidePageDto>>> FieldLayoutReport(
        [FromServices] IQueryHandler<SignFlow.Application.Features.SignatureFields.GetFieldLayoutReportQuery, List<SignFlow.Application.Features.SignatureFields.FieldOutsidePageDto>> handler,
        CancellationToken ct)
        => Ok(await handler.HandleAsync(new SignFlow.Application.Features.SignatureFields.GetFieldLayoutReportQuery(), ct));

    /// <summary>Send a participant a new signing link by e-mail; their previous link stops working (links are never shown)</summary>
    [HttpPost("{id:guid}/recipients/{recipientId:guid}/resend-link")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> ResendSigningLink(
        [FromRoute] Guid id, [FromRoute] Guid recipientId,
        [FromServices] ICommandHandler<SignFlow.Application.Features.Signing.Commands.ResendSigningLinkCommand> handler,
        CancellationToken ct)
    {
        await handler.HandleAsync(new SignFlow.Application.Features.Signing.Commands.ResendSigningLinkCommand(id, recipientId), ct);
        return NoContent();
    }

    /// <summary>Delete a document</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        await deleteHandler.HandleAsync(new DeleteDocumentCommand(id), ct);
        return NoContent();
    }

    /// <summary>Download the signed PDF (as attachment)</summary>
    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Download([FromRoute] Guid id, [FromServices] IQueryHandler<GetDocumentByIdQuery, DocumentDetailDto> docHandler, CancellationToken ct)
    {
        var doc = await docHandler.HandleAsync(new GetDocumentByIdQuery(id), ct);

        // HTML template documents only have a downloadable PDF once signing is complete
        // (FinalStoragePath is set by ApproveSigningCommand after PDF generation)
        if (doc.FinalStoragePath is null && doc.StoragePath.StartsWith("html-templates/"))
            return NotFound(new ProblemDetails { Detail = "PDF is not yet available — the document must be fully signed first." });

        var path = doc.FinalStoragePath ?? doc.StoragePath;
        var stream = await storageService.DownloadAsync(path, ct);
        return File(stream, "application/pdf", doc.FileName);
    }

    /// <summary>
    /// Ask a question about the contract. The answer comes only from the contract's own text and is shown with the
    /// excerpts that support it, each checked word for word. The client keeps the conversation and sends it back.
    /// </summary>
    [HttpPost("{id:guid}/ask")]
    [ProducesResponseType(typeof(ContractChatReplyDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public async Task<ActionResult<ContractChatReplyDto>> Ask(
        [FromRoute] Guid id,
        [FromBody] AskContractQuestionRequest request,
        [FromServices] ICommandHandler<AskContractQuestionCommand, ContractChatReplyDto> askHandler,
        CancellationToken ct)
    {
        try
        {
            var result = await askHandler.HandleAsync(
                new AskContractQuestionCommand(id, request.Question, request.History ?? [], request.Language ?? "fr"), ct);
            return Ok(result);
        }
        catch (AiNotConfiguredException)
        {
            return Problem("Harvey is not configured on this server.", statusCode: 503);
        }
        catch (HttpRequestException)
        {
            return Problem("The language model could not be reached. Please try again.", statusCode: 502);
        }
    }

    /// <summary>The contract's key data read by the AI assistant, each value with the sentence it comes from (checked
    /// word for word). Nothing is saved: apply the term with PATCH {id}/term.</summary>
    [HttpPost("{id:guid}/key-terms")]
    [ProducesResponseType(typeof(KeyTermsDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public Task<ActionResult<KeyTermsDto>> ExtractKeyTerms(
        [FromRoute] Guid id, [FromQuery] string? language,
        [FromServices] ICommandHandler<ExtractKeyTermsCommand, KeyTermsDto> handler, CancellationToken ct) =>
        AiCall(() => handler.HandleAsync(new ExtractKeyTermsCommand(id, language ?? "fr"), ct));

    /// <summary>A short AI decision note for the contract's next deadline: renew, renegotiate or terminate.</summary>
    [HttpPost("{id:guid}/renewal-note")]
    [ProducesResponseType(typeof(RenewalNoteDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public Task<ActionResult<RenewalNoteDto>> WriteRenewalNote(
        [FromRoute] Guid id, [FromQuery] string? language,
        [FromServices] ICommandHandler<WriteRenewalNoteCommand, RenewalNoteDto> handler, CancellationToken ct) =>
        AiCall(() => handler.HandleAsync(new WriteRenewalNoteCommand(id, language ?? "fr"), ct));

    private async Task<ActionResult<T>> AiCall<T>(Func<Task<T>> call)
    {
        try
        {
            return Ok(await call());
        }
        catch (AiNotConfiguredException)
        {
            return Problem("Harvey is not configured on this server.", statusCode: 503);
        }
        catch (HttpRequestException)
        {
            return Problem("The language model could not be reached. Please try again.", statusCode: 502);
        }
    }

    /// <summary>Start Harvey's review of the current version in the background (R-M8-9). Progress and the result arrive
    /// over SignalR ("review-progress"); read them with GET reviews/latest.</summary>
    [HttpPost("{id:guid}/reviews")]
    [ProducesResponseType(typeof(ReviewRunDto), 202)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public async Task<ActionResult<ReviewRunDto>> StartReview(
        [FromRoute] Guid id,
        [FromServices] ICommandHandler<StartReviewCommand, ReviewRunDto> handler,
        CancellationToken ct)
    {
        var run = await handler.HandleAsync(new StartReviewCommand(id), ct);
        return Accepted(run);
    }

    /// <summary>The contract's review: latest run, verdict, scores and findings with their origin</summary>
    [HttpGet("{id:guid}/reviews/latest")]
    [ProducesResponseType(typeof(ReviewDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<ReviewDto>> GetLatestReview(
        [FromRoute] Guid id,
        [FromServices] IQueryHandler<GetLatestReviewQuery, ReviewDto> handler,
        CancellationToken ct)
        => Ok(await handler.HandleAsync(new GetLatestReviewQuery(id), ct));

    /// <summary>Accept a finding, mark it not applicable (both with a reason), or reopen it; the verdict is re-graded</summary>
    [HttpPatch("{id:guid}/review-items/{itemId:guid}")]
    [ProducesResponseType(typeof(ReviewDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<ReviewDto>> DecideReviewItem(
        [FromRoute] Guid id,
        [FromRoute] Guid itemId,
        [FromBody] DecideReviewItemRequest request,
        [FromServices] ICommandHandler<DecideReviewItemCommand, ReviewDto> handler,
        CancellationToken ct)
        => Ok(await handler.HandleAsync(new DecideReviewItemCommand(id, itemId, request.Status, request.Reason), ct));

    /// <summary>Get an AI-generated plain-English summary of a document</summary>
    [HttpGet("{id:guid}/summary")]
    [ProducesResponseType(typeof(AiDocumentSummaryDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<AiDocumentSummaryDto>> GetSummary(
        [FromRoute] Guid id,
        [FromServices] IQueryHandler<GetDocumentSummaryQuery, AiDocumentSummaryDto> summaryHandler,
        CancellationToken ct)
    {
        var result = await summaryHandler.HandleAsync(new GetDocumentSummaryQuery(id), ct);
        return Ok(result);
    }

    /// <summary>Download a specific version of the contract (as attachment)</summary>
    [HttpGet("{id:guid}/versions/{versionNumber:int}/download")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> DownloadVersion(
        [FromRoute] Guid id,
        [FromRoute] int versionNumber,
        [FromServices] IQueryHandler<GetDocumentVersionFileQuery, DocumentVersionFileDto> versionHandler,
        CancellationToken ct)
    {
        var file = await versionHandler.HandleAsync(new GetDocumentVersionFileQuery(id, versionNumber), ct);
        var stream = await storageService.DownloadAsync(file.StoragePath, ct);
        return File(stream, "application/pdf", file.FileName);
    }

    /// <summary>Redline two versions of the contract against each other</summary>
    [HttpGet("{id:guid}/versions/compare")]
    [ProducesResponseType(typeof(VersionComparisonDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<VersionComparisonDto>> CompareVersions(
        [FromRoute] Guid id,
        [FromQuery] int from,
        [FromQuery] int to,
        [FromServices] IQueryHandler<CompareDocumentVersionsQuery, VersionComparisonDto> compareHandler,
        CancellationToken ct)
    {
        var result = await compareHandler.HandleAsync(new CompareDocumentVersionsQuery(id, from, to), ct);
        return Ok(result);
    }

    /// <summary>Stream the original PDF inline (for in-browser rendering)</summary>
    [HttpGet("{id:guid}/stream")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Stream([FromRoute] Guid id, [FromServices] IQueryHandler<GetDocumentByIdQuery, DocumentDetailDto> docHandler, CancellationToken ct)
    {
        var doc = await docHandler.HandleAsync(new GetDocumentByIdQuery(id), ct);
        var stream = await storageService.DownloadAsync(doc.StoragePath, ct);
        // No filename → Content-Disposition: inline, allowing the browser/PDF.js to render it
        return File(stream, "application/pdf", enableRangeProcessing: true);
    }
}

public record UploadDocumentRequest(
    IFormFile File,
    string Title,
    string? Description = null,
    string? Subject = null,
    string? Message = null,
    int? ExpirationDays = null,
    Guid? RequestTypeId = null);

public record ReopenDocumentRequest(int? SlaHours);

public record UpdateObligationRequest(DateTime DueDate, string ObligationType);

public record UpdateContractTermRequest(
    DateTime? StartDate,
    DateTime? EndDate,
    bool AutoRenews,
    int? NoticePeriodDays);

public record ReviseAnswersRequest(Dictionary<string, string>? FilledValues, string? ChangeNote = null);
public record EditPdfRequest(List<PdfTextEdit> Edits, string? ChangeNote = null);
public record UploadRevisionRequest(IFormFile File, string? ChangeNote = null);


/// <param name="Reason">Why the contract goes out with open high-risk findings; required in that case (R-M8-8).</param>
public record SendDocumentRequest(string? Reason = null);

public record DecideReviewItemRequest(ReadinessItemStatus Status, string? Reason);

/// <summary>A question about a contract, with the conversation so far (the client keeps it).</summary>
public record AskContractQuestionRequest(string Question, IReadOnlyList<ChatTurn>? History, string? Language);
