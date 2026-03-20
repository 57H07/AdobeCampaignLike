using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Domain.Entities;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Application service for managing placeholder manifest entries on templates.
/// Responsible for CRUD operations on manifest entries and manifest completeness validation.
/// </summary>
public interface IPlaceholderManifestService
{
    /// <summary>
    /// Returns all placeholder manifest entries declared for the given template.
    /// </summary>
    Task<IReadOnlyList<PlaceholderManifestEntryDto>> GetByTemplateIdAsync(
        Guid templateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new manifest entry for the specified template.
    /// Throws NotFoundException if templateId does not exist.
    /// Throws ValidationException if the key already exists in the manifest.
    /// </summary>
    Task<PlaceholderManifestEntryDto> AddEntryAsync(
        Guid templateId,
        UpsertPlaceholderManifestRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing manifest entry.
    /// Throws NotFoundException if the entry does not exist.
    /// Throws ValidationException if the new key conflicts with another entry in the same template.
    /// </summary>
    Task<PlaceholderManifestEntryDto> UpdateEntryAsync(
        Guid templateId,
        Guid entryId,
        UpsertPlaceholderManifestRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a manifest entry.
    /// Throws NotFoundException if the entry does not exist.
    /// </summary>
    Task DeleteEntryAsync(
        Guid templateId,
        Guid entryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the entire placeholder manifest for a template with the provided entries.
    /// Useful for bulk save from the UI editor.
    /// </summary>
    Task<IReadOnlyList<PlaceholderManifestEntryDto>> ReplaceManifestAsync(
        Guid templateId,
        IEnumerable<UpsertPlaceholderManifestRequest> entries,
        CancellationToken cancellationToken = default);
}
