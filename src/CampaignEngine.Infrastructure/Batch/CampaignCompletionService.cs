using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;

namespace CampaignEngine.Infrastructure.Batch;

/// <summary>
/// Evaluates a completed campaign step and transitions the campaign
/// to the appropriate final status based on business rules:
///
///   - Completed:     failure rate &lt; 2%
///   - PartialFailure: failure rate >= 2% and &lt; 10%
///   - ManualReview:  failure rate >= 10%
///
/// Called by ChunkCoordinatorService.RecordChunkCompletionAsync
/// when the last chunk for a step finishes processing.
/// </summary>
public sealed class CampaignCompletionService : ICampaignCompletionService
{
    private const double PartialFailureThreshold = 0.02;  // 2%
    private const double ManualReviewThreshold = 0.10;    // 10%

    private readonly ICampaignRepository _campaignRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppLogger<CampaignCompletionService> _logger;

    public CampaignCompletionService(
        ICampaignRepository campaignRepository,
        IUnitOfWork unitOfWork,
        IAppLogger<CampaignCompletionService> logger)
    {
        _campaignRepository = campaignRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task FinalizeStepAsync(
        Guid campaignId,
        Guid stepId,
        CancellationToken cancellationToken = default)
    {
        // Serialize concurrent finalizations of the same campaign row.
        // The chunk coordinator uses an atomic "step claim" to ensure only one
        // caller reaches this method per step, but we wrap in a transaction so
        // the campaign counter reload sees a consistent snapshot (Fix #9).
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var campaign = await _campaignRepository.GetWithStepsAsync(campaignId, cancellationToken)
                ?? throw new NotFoundException("Campaign", campaignId);

            var step = campaign.Steps.FirstOrDefault(s => s.Id == stepId)
                ?? throw new NotFoundException("CampaignStep", stepId);

            // Acquire an update lock on the campaign row and reload the latest
            // atomically-incremented counters under that lock.
            await _unitOfWork.ExecuteSqlRawAsync(
                "SELECT Id FROM Campaigns WITH (UPDLOCK, HOLDLOCK) WHERE Id = {0}",
                cancellationToken,
                campaignId);

            await _campaignRepository.ReloadAsync(campaign, cancellationToken);

            var total = campaign.TotalRecipients;
            var failures = campaign.FailureCount;
            var processed = campaign.SuccessCount + campaign.FailureCount;

            CampaignStatus finalStatus;

            // Fix #4: explicit handling of TotalRecipients == 0.
            // Zero recipients with zero failures is a legitimate empty campaign (Completed).
            // Zero recipients with any failures/processed > 0 indicates counter corruption
            // or data-source issues — route to ManualReview rather than silently Completed.
            if (total == 0)
            {
                if (processed == 0 && failures == 0)
                {
                    finalStatus = CampaignStatus.Completed;
                    _logger.LogInformation(
                        "Campaign {CampaignId}: finalized with zero recipients.", campaignId);
                }
                else
                {
                    finalStatus = CampaignStatus.ManualReview;
                    _logger.LogWarning(
                        "Campaign {CampaignId}: TotalRecipients = 0 but Processed={Processed}, Failures={Failures} — entering ManualReview",
                        campaignId, processed, failures);
                }
            }
            else
            {
                var failureRate = (double)failures / total;

                if (failureRate >= ManualReviewThreshold)
                {
                    finalStatus = CampaignStatus.ManualReview;
                    _logger.LogWarning(
                        "Campaign {CampaignId}: failure rate {Rate:P1} >= {Threshold:P0} — entering ManualReview",
                        campaignId, failureRate, ManualReviewThreshold);
                }
                else if (failureRate >= PartialFailureThreshold)
                {
                    finalStatus = CampaignStatus.PartialFailure;
                    _logger.LogWarning(
                        "Campaign {CampaignId}: failure rate {Rate:P1} >= {Threshold:P0} — PartialFailure",
                        campaignId, failureRate, PartialFailureThreshold);
                }
                else
                {
                    finalStatus = CampaignStatus.Completed;
                    _logger.LogInformation(
                        "Campaign {CampaignId}: completed successfully. Sent={Success}, Failed={Failures}, Rate={Rate:P1}",
                        campaignId, campaign.SuccessCount, failures, failureRate);
                }
            }

            campaign.Status = finalStatus;
            campaign.CompletedAt = DateTime.UtcNow;

            // ExecutedAt was set atomically by the chunk coordinator's finalization claim;
            // keep the step refresh fallback for older code paths that call this directly.
            if (step.ExecutedAt is null)
                step.ExecutedAt = DateTime.UtcNow;

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation(
                "Campaign {CampaignId} finalized. Status={Status}, Total={Total}, Success={Success}, Failed={Failed}",
                campaignId, finalStatus, total, campaign.SuccessCount, failures);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
