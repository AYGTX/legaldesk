using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Services;

/// <summary>A revision ends the round it interrupts (R-M7-2): its steps, decisions and signatures stay as they were,
/// and the next send opens a new round on the new version.</summary>
public static class CircuitRounds
{
    /// <summary>Call before the participants are reset for the revision. The document's recipients must be loaded.</summary>
    public static async Task CloseForRevisionAsync(IApplicationDbContext db, Document document, CancellationToken ct = default)
    {
        var round = await db.ContractRounds.Where(r => r.DocumentId == document.Id).OrderByDescending(r => r.Number).FirstOrDefaultAsync(ct);
        round?.Close(RoundOutcome.Superseded);

        var workflow = document.Workflow ?? await db.Workflows.FirstOrDefaultAsync(w => w.DocumentId == document.Id, ct);
        workflow?.NewRound();

        // A stand-in leaves with the round: the fields handed to them go back to the participant they stood in for.
        var byId = document.Recipients.ToDictionary(r => r.Id);
        var standIns = document.Recipients.Where(r => r.DelegatedFromRecipientId is not null).Select(r => r.Id).ToList();
        if (standIns.Count == 0) return;

        var fields = await db.SignatureFields.Where(f => f.RecipientId != null && standIns.Contains(f.RecipientId.Value)).ToListAsync(ct);
        foreach (var field in fields)
        {
            var origin = byId[field.RecipientId!.Value];
            while (origin.DelegatedFromRecipientId is { } from && byId.TryGetValue(from, out var earlier))
                origin = earlier;
            field.AssignToRecipient(origin.Id);
        }
    }
}
