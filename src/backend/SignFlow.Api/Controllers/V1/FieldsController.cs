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
public class FieldsController(ICommandHandler<SaveFieldsCommand> saveHandler) : ControllerBase
{
    /// <summary>Save all signature fields for a document (replaces existing fields)</summary>
    [HttpPut]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> SaveFields([FromRoute] Guid documentId, [FromBody] List<FieldDto> fields, CancellationToken ct)
    {
        await saveHandler.HandleAsync(new SaveFieldsCommand(documentId, fields), ct);
        return NoContent();
    }
}
