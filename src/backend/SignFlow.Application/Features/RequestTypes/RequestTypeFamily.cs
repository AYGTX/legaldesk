using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.RequestTypes;

internal static class RequestTypeFamily
{
    internal static async Task CheckPresetAsync(IApplicationDbContext db, Guid? presetId, CancellationToken ct)
    {
        if (presetId is { } id && !await db.WorkflowPresets.AnyAsync(p => p.Id == id && p.IsActive, ct))
            throw new Common.Exceptions.ValidationException("The selected workflow is invalid or inactive.");
    }
}
