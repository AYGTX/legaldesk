using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Services;

/// <summary>One rung of the ladder that is due: the date it counts towards and how many days before it, or the end date itself.</summary>
public record LadderRung(DateTime Anchor, int Offset, bool IsEnd)
{
    /// <summary>The day itself and the end date pull in the contract's team and the managers (R-M10-3).</summary>
    public bool Escalates => IsEnd || Offset == 0;
}

/// <summary>
/// The alert ladder (R-M10-3), as a rule over dates a person confirmed — never a guess. It counts towards the notice
/// deadline when there is one, else the end date: by default 60, 30, 14 and 7 days before, the day itself, then the end
/// date. Each rung is sent once per anchor; a term entered late jumps to the rung it is at, instead of replaying the
/// earlier ones. A recorded decision stops it (R-M10-4).
/// </summary>
public static class TermLadder
{
    public static readonly int[] DefaultOffsets = [60, 30, 14, 7, 0];

    /// <summary>The organisation's offsets ("60,30,14,7,0"), most distant first, the day itself always included.</summary>
    public static int[] Offsets(string? configured)
    {
        var parsed = (configured ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.TryParse(v, out var n) ? n : -1).Where(n => n is >= 0 and <= 365).Distinct().ToList();
        if (parsed.Count == 0) return DefaultOffsets;
        if (!parsed.Contains(0)) parsed.Add(0);
        return [.. parsed.OrderDescending()];
    }

    public static LadderRung? Due(ContractTerm term, DateTime today, IReadOnlyList<int> offsets)
    {
        if (!term.IsConfirmed || term.Decision != TermDecision.None) return null;
        if (term.Anchor is not { } anchor || term.CurrentEndDate is not { } end) return null;

        today = today.Date;
        var daysToAnchor = (anchor - today).Days;

        if (daysToAnchor >= 0)
        {
            // The most urgent rung already reached; nothing until the first one.
            var reached = offsets.Where(o => daysToAnchor <= o).DefaultIfEmpty(-1).Min();
            if (reached < 0) return null;
            if (term.LastAlertAnchor == anchor && term.LastAlertOffset is { } sent && sent <= reached) return null;
            return new LadderRung(anchor, reached, false);
        }

        // Past the notice deadline: the day itself, if it was never sent (a term recorded late), then the end date.
        if (today < end)
        {
            if (term.LastAlertAnchor == anchor && term.LastAlertOffset == 0) return null;
            return new LadderRung(anchor, 0, false);
        }
        return term.EndAlertSent ? null : new LadderRung(end, 0, true);
    }

    /// <summary>The term's state for the screens and reports, from the same dates.</summary>
    public static ContractTermStatus Status(ContractTerm? term, DateTime asOf) =>
        term is null
            ? new ContractTermStatus(LifecycleStage.Unknown, null, null, null)
            : ContractTermEvaluator.Evaluate(term.StartDate, term.CurrentEndDate, term.NoticeDeadline, asOf);
}
