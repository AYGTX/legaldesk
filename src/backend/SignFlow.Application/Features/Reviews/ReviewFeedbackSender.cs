using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Serialization;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Features.Reviews;

public interface IReviewFeedbackSender
{
    Task SendAsync(Guid itemId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sends a "not applicable" finding to Harvey (R-M8-4): the rule, its version, the clause reference, the quoted words
/// and the reason. Never the contract. Throws when Harvey is down, so the background job retries.
/// </summary>
public class ReviewFeedbackSender(IApplicationDbContext db, ILegalCorpusClient harvey) : IReviewFeedbackSender
{
    public async Task SendAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await db.DocumentReadinessItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        // Reopened since: nothing left to report.
        if (item?.RuleKey is null || item.Status != ReadinessItemStatus.NotApplicable || item.Reason is null)
            return;

        var quotes = item.QuotesJson is null ? null : JsonSerializer.Deserialize<List<string>>(item.QuotesJson, ContractJson.Options);
        await harvey.SendFeedbackAsync(new ReviewFeedbackReport(
            item.Id.ToString(), item.RuleKey, item.RuleVersion, item.ClauseReference,
            quotes is { Count: > 0 } ? string.Join(" … ", quotes) : null, item.Reason), cancellationToken);
    }
}
