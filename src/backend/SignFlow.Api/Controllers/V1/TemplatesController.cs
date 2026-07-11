using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Templates.Commands;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Document template endpoints</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[Authorize]
public class TemplatesController(ICommandHandler<CreateTemplateCommand, Guid> createHandler) : ControllerBase
{
    /// <summary>Upload a new document template</summary>
    [HttpPost]
    [RequestSizeLimit(100 * 1024 * 1024)]
    [ProducesResponseType(typeof(Guid), 201)]
    public async Task<ActionResult<Guid>> Create([FromForm] CreateTemplateRequest request, CancellationToken ct)
    {
        var command = new CreateTemplateCommand(
            request.File.OpenReadStream(),
            request.File.FileName,
            request.File.Length,
            request.File.ContentType,
            request.Name,
            request.Description,
            request.Category,
            request.IsPublic);

        var id = await createHandler.HandleAsync(command, ct);
        return StatusCode(201, id);
    }
}

public record CreateTemplateRequest(
    IFormFile File,
    string Name,
    string? Description = null,
    string? Category = null,
    bool IsPublic = false);
