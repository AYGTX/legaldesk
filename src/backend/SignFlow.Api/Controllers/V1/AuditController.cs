using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Features.AuditTrail.Queries;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Audit trail endpoints</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/documents/{documentId:guid}/[controller]")]
[ApiController]
[Authorize]
public class AuditController(
    IQueryHandler<GetAuditTrailQuery, PagedResult<AuditLogDto>> auditHandler,
    IQueryHandler<ExportAuditTrailQuery, byte[]> exportHandler) : ControllerBase
{
    /// <summary>Get audit trail for a document</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AuditLogDto>), 200)]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> GetAudit(
        [FromRoute] Guid documentId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await auditHandler.HandleAsync(new GetAuditTrailQuery(documentId, pageNumber, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Export the full audit trail for a document as a CSV file</summary>
    [HttpGet("export")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Export([FromRoute] Guid documentId, CancellationToken ct)
    {
        var csv = await exportHandler.HandleAsync(new ExportAuditTrailQuery(documentId), ct);
        return File(csv, "text/csv", $"audit-trail-{documentId}.csv");
    }
}
