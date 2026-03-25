using CampaignEngine.Application.DTOs.Campaigns;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Service responsible for creating and retrieving template snapshots.
///
/// Business rules:
/// - A snapshot is created for every step template when a campaign is scheduled.
/// - Snapshots are immutable after creation.
/// - Snapshots include fully resolved sub-template content.
/// - Snapshots persist even if the original template is later deleted.
/// </summary>
public interface ITemplateSnapshotService
{
    /// <summary>
    /// Creates immutable snapshots for all steps of the given campaign.
    /// Each step's <see cref="CampaignEngine.Domain.Entities.CampaignStep.TemplateSnapshotId"/>
    /// is set to the newly created snapshot.
    /// Should be called when a campaign transitions to Scheduled status.
    /// </summary>
    /// <param name="campaignId">The campaign whose steps need snapshots.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateSnapshotsForCampaignAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all snapshots associated with the given campaign (one per step),
    /// ordered by step order.
    /// Returns an empty list if the campaign has no snapshots yet (e.g. still in Draft).
    /// </summary>
    /// <param name="campaignId">The campaign ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<TemplateSnapshotDto>> GetSnapshotsForCampaignAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);
}
