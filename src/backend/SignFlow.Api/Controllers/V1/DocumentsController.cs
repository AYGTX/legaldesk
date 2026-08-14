using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Features.Documents.Commands;
using SignFlow.Application.Features.Documents.Queries;
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
        CancellationToken ct = default)
    {
        var query = new GetDocumentsQuery(pageNumber, Math.Min(pageSize, 100), search, status, sortBy, descending, requestTypeId);
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
    [RequestSizeLimit(100 * 1024 * 1024)]
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
    [RequestSizeLimit(100 * 1024 * 1024)]
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
        await sendHandler.HandleAsync(new SendDocumentCommand(id, request?.Force ?? false), ct);
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

    /// <summary>Run a full legal review of the contract — gaps, weak clauses, proposed wording and the next obligation date</summary>
    [HttpPost("{id:guid}/analyze")]
    [ProducesResponseType(typeof(ContractAnalysisDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<ContractAnalysisDto>> Analyze(
        [FromRoute] Guid id,
        [FromServices] ICommandHandler<AnalyzeContractCommand, ContractAnalysisDto> analyzeHandler,
        CancellationToken ct)
    {
        var result = await analyzeHandler.HandleAsync(new AnalyzeContractCommand(id), ct);
        return Ok(result);
    }

    /// <summary>Check the contract against the published regulatory watch registry — a keyword scan, not a legal opinion</summary>
    [HttpPost("{id:guid}/compliance")]
    [ProducesResponseType(typeof(ComplianceAnalysisDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<ComplianceAnalysisDto>> AnalyzeCompliance(
        [FromRoute] Guid id,
        [FromServices] ICommandHandler<AnalyzeComplianceCommand, ComplianceAnalysisDto> analyzeComplianceHandler,
        CancellationToken ct)
    {
        var result = await analyzeComplianceHandler.HandleAsync(new AnalyzeComplianceCommand(id), ct);
        return Ok(result);
    }

    /// <summary>Pre-flight review: run the legal and regulatory checks, then grade whether the contract is fit to send</summary>
    [HttpPost("{id:guid}/readiness")]
    [ProducesResponseType(typeof(ReadinessDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<ReadinessDto>> EvaluateReadiness(
        [FromRoute] Guid id,
        [FromServices] ICommandHandler<EvaluateReadinessCommand, ReadinessDto> readinessHandler,
        CancellationToken ct)
    {
        var result = await readinessHandler.HandleAsync(new EvaluateReadinessCommand(id), ct);
        return Ok(result);
    }

    /// <summary>Get the stored pre-flight verdict and its to-do list without re-running the analysis</summary>
    [HttpGet("{id:guid}/readiness")]
    [ProducesResponseType(typeof(ReadinessDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<ReadinessDto>> GetReadiness(
        [FromRoute] Guid id,
        [FromServices] IQueryHandler<GetReadinessQuery, ReadinessDto> readinessQueryHandler,
        CancellationToken ct)
    {
        var result = await readinessQueryHandler.HandleAsync(new GetReadinessQuery(id), ct);
        return Ok(result);
    }

    /// <summary>Resolve or dismiss one pre-flight to-do item — the verdict is re-graded from what stays open</summary>
    [HttpPatch("{id:guid}/readiness/items/{itemId:guid}")]
    [ProducesResponseType(typeof(ReadinessDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<ReadinessDto>> SetReadinessItemStatus(
        [FromRoute] Guid id,
        [FromRoute] Guid itemId,
        [FromBody] SetReadinessItemStatusRequest request,
        [FromServices] ICommandHandler<SetReadinessItemStatusCommand, ReadinessDto> setItemHandler,
        CancellationToken ct)
    {
        var result = await setItemHandler.HandleAsync(
            new SetReadinessItemStatusCommand(id, itemId, request.Status, request.Note), ct);
        return Ok(result);
    }

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

public record UploadRevisionRequest(IFormFile File, string? ChangeNote = null);

public record SetReadinessItemStatusRequest(ReadinessItemStatus Status, string? Note = null);

/// <summary>Force lets the owner send past a "needs work" verdict after acknowledging it.</summary>
public record SendDocumentRequest(bool Force = false);
