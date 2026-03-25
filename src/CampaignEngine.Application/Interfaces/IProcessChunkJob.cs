namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Hangfire background job interface for processing a single recipient chunk.
/// Implemented by ProcessChunkJob in the Infrastructure layer.
/// Using an interface here allows the Application layer to reference it
/// without depending on Hangfire directly.
/// </summary>
public interface IProcessChunkJob
{
    /// <summary>
    /// Processes all recipients in the specified chunk.
    /// Renders the template snapshot and dispatches messages via the channel dispatcher.
    /// Reports completion or failure to IChunkCoordinatorService.
    /// Auto-retried by Hangfire up to MaxRetryAttempts if an unhandled exception occurs.
    /// </summary>
    /// <param name="chunkId">The CampaignChunk ID to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecuteAsync(Guid chunkId, CancellationToken cancellationToken);
}
