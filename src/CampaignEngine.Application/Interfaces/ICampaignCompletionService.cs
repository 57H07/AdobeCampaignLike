namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Detects when all chunks for a campaign step have been processed
/// and transitions the campaign to the appropriate final status.
/// Business rules:
///   - PartialFailure if failure rate exceeds 2% of total recipients.
///   - ManualReview if failure rate exceeds 10% of total recipients.
///   - Completed if failure rate is under 2%.
/// </summary>
public interface ICampaignCompletionService
{
    /// <summary>
    /// Evaluates the final status of a campaign step and updates
    /// the campaign's aggregate counters and status accordingly.
    /// Called by the chunk coordinator when the last chunk completes.
    /// </summary>
    /// <param name="campaignId">Campaign to finalize.</param>
    /// <param name="stepId">Step that just completed all chunks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task FinalizeStepAsync(
        Guid campaignId,
        Guid stepId,
        CancellationToken cancellationToken = default);
}
