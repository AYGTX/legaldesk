using Hangfire;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.Reviews;

namespace SignFlow.Infrastructure.Services.Background;

/// <summary>Queues the review's background work on Hangfire.</summary>
public class HangfireReviewQueue(IBackgroundJobClient jobs) : IReviewQueue
{
    public void EnqueueReview(Guid runId, Guid organisationId) =>
        jobs.Enqueue<ReviewRunJob>(j => j.RunAsync(runId, organisationId, CancellationToken.None));

    public void EnqueueFeedback(Guid itemId, Guid organisationId) =>
        jobs.Enqueue<ReviewFeedbackJob>(j => j.SendAsync(itemId, organisationId, CancellationToken.None));
}

/// <summary>
/// One review run, for the organisation it was queued for. Never retried by Hangfire: a failed run is shown as failed
/// and the owner starts a new one (R-M8-9), so a review never changes a contract long after it was asked for.
/// </summary>
public class ReviewRunJob(ITenantContext tenant, IReviewRunner runner)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid runId, Guid organisationId, CancellationToken cancellationToken)
    {
        using (tenant.BeginScope(organisationId))
            await runner.RunAsync(runId, cancellationToken);
    }
}

/// <summary>Delivers a "not applicable" report to Harvey; retried by Hangfire until Harvey takes it.</summary>
public class ReviewFeedbackJob(ITenantContext tenant, IReviewFeedbackSender sender)
{
    [AutomaticRetry(Attempts = 10)]
    public async Task SendAsync(Guid itemId, Guid organisationId, CancellationToken cancellationToken)
    {
        using (tenant.BeginScope(organisationId))
            await sender.SendAsync(itemId, cancellationToken);
    }
}
