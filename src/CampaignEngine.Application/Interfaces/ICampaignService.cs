using CampaignEngine.Application.DTOs.Campaigns;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Application service for managing campaign lifecycle.
/// Campaign creation, retrieval, and status management.
/// Operator and Admin roles can create and manage campaigns.
/// </summary>
public interface ICampaignService
{
    /// <summary>
    /// Creates a new campaign in Draft status.
    /// Validates: unique name, published templates only, schedule at least 5 minutes in future.
    /// </summary>
    Task<CampaignDto> CreateAsync(CreateCampaignRequest request, string? createdBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paginated, filtered list of campaigns.
    /// </summary>
    Task<CampaignPagedResult> GetPagedAsync(CampaignFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single campaign by ID, including its steps.
    /// Returns null if not found.
    /// </summary>
    Task<CampaignDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions a campaign from Draft to Scheduled status.
    /// Creates immutable template snapshots for all steps (US-025).
    /// Business rules:
    ///   - Campaign must be in Draft status.
    ///   - ScheduledAt must be set and at least 5 minutes in the future.
    ///   - Snapshot is created atomically with the status change.
    /// Throws NotFoundException if campaign not found.
    /// Throws ValidationException if transition is not allowed.
    /// </summary>
    Task<CampaignDto> ScheduleAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a real-time status snapshot for a campaign, including progress counters
    /// and the full status transition history.
    /// Returns null if the campaign is not found.
    /// </summary>
    Task<CampaignStatusDto?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default);
}
