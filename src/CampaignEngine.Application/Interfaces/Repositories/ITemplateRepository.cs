using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces.Repositories;

/// <summary>
/// Repository for Template aggregate (including history, snapshots, sub-templates).
/// </summary>
public interface ITemplateRepository : IRepository<Template>
{
    /// <summary>
    /// Returns all non-deleted templates matching optional channel/status filters,
    /// ordered by Channel then Name. AsNoTracking.
    /// </summary>
    Task<(IReadOnlyList<Template> Items, int Total)> GetPagedAsync(
        ChannelType? channel,
        TemplateStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single non-deleted template by ID. AsNoTracking.
    /// Returns null if not found or soft-deleted.
    /// </summary>
    Task<Template?> GetByIdNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a tracked template (for mutations). Does NOT IgnoreQueryFilters.
    /// Returns null if soft-deleted or not found.
    /// </summary>
    Task<Template?> GetTrackedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all non-deleted sub-templates ordered by Channel then Name. AsNoTracking.
    /// </summary>
    Task<IReadOnlyList<Template>> GetSubTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a non-deleted template with the given name and channel exists,
    /// optionally excluding the specified ID (for self-update validation).
    /// </summary>
    Task<bool> NameExistsAsync(
        string name,
        ChannelType channel,
        Guid? excludeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a template exists, even if soft-deleted.
    /// Used when history access requires confirmation the template ever existed.
    /// </summary>
    Task<bool> ExistsIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a tracked template ignoring soft-delete filter. Used for Diff/Revert.
    /// </summary>
    Task<Template?> GetIgnoreQueryFiltersAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a list of templates by IDs (used by TemplateSnapshotService).
    /// Tracked entities.
    /// </summary>
    Task<IReadOnlyList<Template>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    // ----------------------------------------------------------------
    // TemplateHistory
    // ----------------------------------------------------------------

    /// <summary>
    /// Adds a TemplateHistory snapshot to the change tracker.
    /// </summary>
    Task AddHistoryAsync(TemplateHistory history, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the version history for a template, ordered descending. AsNoTracking.
    /// </summary>
    Task<IReadOnlyList<TemplateHistory>> GetHistoryAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a specific history entry by templateId and version. AsNoTracking.
    /// </summary>
    Task<TemplateHistory?> GetHistoryEntryAsync(Guid templateId, int version, CancellationToken cancellationToken = default);

    // ----------------------------------------------------------------
    // TemplateSnapshot
    // ----------------------------------------------------------------

    /// <summary>Adds a TemplateSnapshot to the change tracker.</summary>
    Task AddSnapshotAsync(TemplateSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns ordered campaign steps that have a TemplateSnapshot,
    /// including the TemplateSnapshot navigation property.
    /// </summary>
    Task<IReadOnlyList<CampaignStep>> GetCampaignStepsWithSnapshotsAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);
}
