using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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

    private readonly CampaignEngineDbContext _dbContext;
    private readonly IAppLogger<CampaignCompletionService> _logger;

    public CampaignCompletionService(
        CampaignEngineDbContext dbContext,
        IAppLogger<CampaignCompletionService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task FinalizeStepAsync(
        Guid campaignId,
        Guid stepId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await _dbContext.Campaigns
            .Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken);

        if (campaign is null)
            throw new NotFoundException("Campaign", campaignId);

        var step = campaign.Steps.FirstOrDefault(s => s.Id == stepId);
        if (step is null)
            throw new NotFoundException("CampaignStep", stepId);

        // Mark the step as executed
        step.ExecutedAt = DateTime.UtcNow;

        // ----------------------------------------------------------------
        // Determine final campaign status based on failure rate
        // ----------------------------------------------------------------
        var total = campaign.TotalRecipients;
        var failures = campaign.FailureCount;

        // Refresh from DB to get atomically-updated counters
        await _dbContext.Entry(campaign).ReloadAsync(cancellationToken);
        total = campaign.TotalRecipients;
        failures = campaign.FailureCount;

        var failureRate = total > 0 ? (double)failures / total : 0.0;

        CampaignStatus finalStatus;

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

        campaign.Status = finalStatus;
        campaign.CompletedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Campaign {CampaignId} finalized. Status={Status}, Total={Total}, Success={Success}, Failed={Failed}",
            campaignId, finalStatus, total, campaign.SuccessCount, failures);
    }
}
