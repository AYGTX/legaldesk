using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Reports.Queries;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Organization-wide reporting and analytics — administrators and managers only</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[Authorize(Roles = "Administrator,Manager")]
public class ReportsController(
    IQueryHandler<GetAdminReportQuery, AdminReportDto> reportHandler) : ControllerBase
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
}
