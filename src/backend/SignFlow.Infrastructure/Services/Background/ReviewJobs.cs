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

    public void EnqueueIntake(Guid documentId, Guid organisationId, bool suggest) =>
        jobs.Enqueue<ContractIntakeJob>(j => j.RunAsync(documentId, organisationId, suggest, CancellationToken.None));
}

/// <summary>Queues work on completed contracts.</summary>
public class HangfireDocumentJobs(IBackgroundJobClient jobs) : SignFlow.Application.Features.Documents.Commands.IDocumentJobs
{
    public void EnqueueSeal(Guid documentId, Guid organisationId) =>
        jobs.Enqueue<SealDocumentJob>(j => j.RunAsync(documentId, organisationId, CancellationToken.None));
}

/// <summary>Seals a completed contract. Retried by Hangfire: a timestamp authority or certificate that is briefly
/// unavailable must not leave a contract unsealed.</summary>
public class SealDocumentJob(ITenantContext tenant, SignFlow.Application.Features.Documents.Commands.IDocumentSealer sealer)
{
    [AutomaticRetry(Attempts = 10)]
    public async Task RunAsync(Guid documentId, Guid organisationId, CancellationToken cancellationToken)
    {
        using (tenant.BeginScope(organisationId))
            await sealer.SealAsync(documentId, cancellationToken);
    }
}

/// <summary>Reads a new contract file and reviews it (R-M2-3). Not retried: a failed review is shown and started again by
/// the owner, and the intake flag lapses so a review can start.</summary>
public class ContractIntakeJob(ITenantContext tenant, SignFlow.Application.Features.Documents.Commands.IContractIntake intake)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid documentId, Guid organisationId, bool suggest, CancellationToken cancellationToken)
    {
        using (tenant.BeginScope(organisationId))
            await intake.RunAsync(documentId, suggest, cancellationToken);
    }
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
