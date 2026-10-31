using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Documents.Queries;

namespace SignFlow.Api.Controllers.V1;

/// <summary>
/// Public check of a sealed contract (R-M5-9): the code printed on its evidence page says who sealed it and when; an
/// uploaded PDF is compared with the sealed one through its SHA-256. Nothing of the contract is shown, nothing is kept.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/verify")]
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimits.Verify)]
public class VerifyController(
    IQueryHandler<VerifySealQuery, SealInfoDto> infoHandler,
    ICommandHandler<CheckSealedFileCommand, SealCheckDto> checkHandler) : ControllerBase
{
    /// <summary>Who sealed the contract with this code, and when</summary>
    [HttpGet("{code}")]
    [ProducesResponseType(typeof(SealInfoDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<SealInfoDto>> Get([FromRoute] string code, CancellationToken ct)
        => Ok(await infoHandler.HandleAsync(new VerifySealQuery(code), ct));

    /// <summary>Does this PDF match the sealed contract, byte for byte?</summary>
    [HttpPost("{code}")]
    [RequestSizeLimit(60 * 1024 * 1024)]
    [ProducesResponseType(typeof(SealCheckDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<SealCheckDto>> Check([FromRoute] string code, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return Ok(await checkHandler.HandleAsync(new CheckSealedFileCommand(code, stream), ct));
    }
}
