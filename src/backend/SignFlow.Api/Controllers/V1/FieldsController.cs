using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.SignatureFields.Commands;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Signature field designer endpoints</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/documents/{documentId:guid}/[controller]")]
[ApiController]
[Authorize]
public class FieldsController(ICommandHandler<SaveFieldsCommand, int> saveHandler) : ControllerBase
{
    /// <summary>
    /// Save the field layout (replaces it). Coordinates are PDF points from the top-left of the page as displayed.
    /// Send the layoutVersion you loaded: a layout saved since answers 409 (code layout.stale).
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(SaveFieldsResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<ActionResult<SaveFieldsResponse>> SaveFields([FromRoute] Guid documentId, [FromBody] SaveFieldsRequest request, CancellationToken ct)
        => Ok(new SaveFieldsResponse(await saveHandler.HandleAsync(new SaveFieldsCommand(documentId, request.Fields ?? [], request.LayoutVersion), ct)));
}

public record SaveFieldsRequest(int? LayoutVersion, List<FieldDto>? Fields);

public record SaveFieldsResponse(int LayoutVersion);
