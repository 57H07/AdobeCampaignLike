using CampaignEngine.Domain.Entities;

namespace CampaignEngine.Application.Interfaces.Repositories;

/// <summary>
/// Repository for PlaceholderManifestEntry entities.
/// </summary>
public interface IPlaceholderManifestRepository : IRepository<PlaceholderManifestEntry>
{
    /// <summary>
    /// Returns all manifest entries for a template, ordered by Key.
    /// Pass noTracking = true for read-only queries.
    /// </summary>
    Task<IReadOnlyList<PlaceholderManifestEntry>> GetByTemplateIdAsync(
        Guid templateId,
        bool noTracking = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single entry by its ID and template ID (ownership check).
    /// Tracked. Returns null if not found.
    /// </summary>
    Task<PlaceholderManifestEntry?> GetByIdAndTemplateIdAsync(
        Guid id,
        Guid templateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if an entry with the given key already exists for this template.
    /// Pass excludeEntryId to exclude a specific entry (for update uniqueness check).
    /// </summary>
    Task<bool> KeyExistsAsync(Guid templateId, string key, Guid? excludeEntryId = null, CancellationToken cancellationToken = default);

    /// <summary>Marks a range of entries for deletion in the change tracker.</summary>
    void RemoveRange(IEnumerable<PlaceholderManifestEntry> entries);

    /// <summary>Adds a range of entries to the change tracker.</summary>
    Task AddRangeAsync(IEnumerable<PlaceholderManifestEntry> entries, CancellationToken cancellationToken = default);
}
