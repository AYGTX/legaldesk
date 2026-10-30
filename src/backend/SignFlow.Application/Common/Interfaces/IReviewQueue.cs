namespace SignFlow.Application.Common.Interfaces;

/// <summary>
/// Background work of the pre-flight review (R-M8-9): the review itself, and the "not applicable" reports sent to
/// Harvey. Each job acts for the organisation it was queued for.
/// </summary>
public interface IReviewQueue
{
    void EnqueueReview(Guid runId, Guid organisationId);

    void EnqueueFeedback(Guid itemId, Guid organisationId);

    /// <summary>Reads a new contract file (text recognition when needed), then reviews it (R-M2-3).</summary>
    void EnqueueIntake(Guid documentId, Guid organisationId, bool suggest);
}
