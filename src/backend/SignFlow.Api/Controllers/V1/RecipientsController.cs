using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Recipients.Commands;
using SignFlow.Domain.Enums;

namespace SignFlow.Api.Controllers.V1;

/// <summary>Recipient management endpoints</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/documents/{documentId:guid}/[controller]")]
[ApiController]
[Authorize]
public class RecipientsController(ICommandHandler<ManageRecipientsCommand> manageHandler) : ControllerBase
{
    /// <summary>Save all recipients and workflow type for a document (replaces existing recipients)</summary>
    [HttpPut]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> ManageRecipients([FromRoute] Guid documentId, [FromBody] SaveRecipientsRequest request, CancellationToken ct)
    {
        await manageHandler.HandleAsync(new ManageRecipientsCommand(documentId, request.WorkflowType, request.Recipients, request.HideRecipientIdentities), ct);
        return NoContent();
    }
}

public record SaveRecipientsRequest(WorkflowType WorkflowType, List<RecipientInputDto> Recipients, bool HideRecipientIdentities = false);
