using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Features.AuditTrail.Queries;
using SignFlow.Application.Features.Reports.Queries;
using SignFlow.Domain.Enums;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Organization-wide reporting and analytics — administrators and managers only</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[Authorize(Roles = "Administrator,Manager")]
public class ReportsController(
    IQueryHandler<GetAdminReportQuery, AdminReportDto> reportHandler,
    IQueryHandler<GetOrgAuditLogQuery, PagedResult<OrgAuditLogDto>> auditLogHandler) : ControllerBase
{
    /// <summary>Get the organization-wide reporting overview (volume, outcomes, SLA, revision activity)</summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(AdminReportDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<ActionResult<AdminReportDto>> GetOverview(CancellationToken ct)
    {
        var result = await reportHandler.HandleAsync(new GetAdminReportQuery(), ct);
        return Ok(result);
    }

    /// <summary>Get the organization-wide audit log across all documents</summary>
    [HttpGet("audit-log")]
    [ProducesResponseType(typeof(PagedResult<OrgAuditLogDto>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<ActionResult<PagedResult<OrgAuditLogDto>>> GetAuditLog(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        [FromQuery] AuditAction? action = null,
        CancellationToken ct = default)
    {
        var result = await auditLogHandler.HandleAsync(new GetOrgAuditLogQuery(pageNumber, pageSize, search, action), ct);
        return Ok(result);
    }
}
