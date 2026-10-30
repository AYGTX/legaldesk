using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

/// <summary>Keeps what the review would queue on Hangfire, so a scenario runs each job itself, when it chooses.</summary>
public sealed class RecordingReviewQueue : IReviewQueue
{
    public List<Guid> Reviews { get; } = [];
    public List<Guid> Feedback { get; } = [];

    public void EnqueueReview(Guid runId, Guid organisationId) => Reviews.Add(runId);

    public void EnqueueFeedback(Guid itemId, Guid organisationId) => Feedback.Add(itemId);
}
