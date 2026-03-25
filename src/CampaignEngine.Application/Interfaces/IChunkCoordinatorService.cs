namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Orchestrates splitting a campaign step's recipients into chunks,
/// enqueuing Hangfire jobs, and tracking completion atomically.
///
/// The Chunk Coordinator pattern is used because Hangfire Community
/// edition lacks built-in batch primitives (requires Hangfire Pro).
/// Instead, each chunk job reports completion via an atomic SQL counter
/// and the last chunk to complete triggers campaign-level finalization.
/// </summary>
public interface IChunkCoordinatorService
{
    /// <summary>
    /// Splits campaign step recipients into chunks and enqueues one Hangfire
    /// job per chunk. Sets campaign status to Running and step status to
    /// StepInProgress. Returns the number of chunks created.
    /// </summary>
    /// <param name="campaignId">The campaign to start processing.</param>
    /// <param name="stepId">The specific step to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of chunks created and enqueued.</returns>
    Task<int> StartCampaignStepAsync(
        Guid campaignId,
        Guid stepId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments the chunk completion counter and detects
    /// when all chunks for a step have finished. If all chunks are done,
    /// triggers campaign completion or step transition logic.
    /// Uses SQL UPDATE...OUTPUT to ensure exactly-once completion detection.
    /// </summary>
    /// <param name="chunkId">The completed chunk's identifier.</param>
    /// <param name="successCount">Recipients processed successfully in this chunk.</param>
    /// <param name="failureCount">Recipients that failed in this chunk.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if this was the last chunk (triggers campaign completion).</returns>
    Task<bool> RecordChunkCompletionAsync(
        Guid chunkId,
        int successCount,
        int failureCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a chunk as failed and schedules retry if attempts remain.
    /// After MaxRetryAttempts, marks chunk as permanently failed.
    /// </summary>
    Task RecordChunkFailureAsync(
        Guid chunkId,
        string errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns current progress for a campaign: processed/total recipients.
    /// </summary>
    Task<CampaignProgressResult> GetProgressAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Real-time progress snapshot for a campaign's batch execution.
/// </summary>
public record CampaignProgressResult(
    Guid CampaignId,
    int TotalRecipients,
    int ProcessedCount,
    int SuccessCount,
    int FailureCount,
    int TotalChunks,
    int CompletedChunks,
    int PendingChunks,
    int FailedChunks,
    string Status);
