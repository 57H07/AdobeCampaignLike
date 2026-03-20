using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Templates;

/// <summary>
/// Infrastructure implementation of IPlaceholderManifestService.
/// Persists placeholder manifest entries for templates via EF Core.
/// Business rules enforced here:
///   - Keys must be unique within a template's manifest.
///   - FreeField placeholders default IsFromDataSource to false.
///   - Template must exist before adding manifest entries.
/// </summary>
public sealed class PlaceholderManifestService : IPlaceholderManifestService
{
    private readonly CampaignEngineDbContext _dbContext;
    private readonly IAppLogger<PlaceholderManifestService> _logger;

    public PlaceholderManifestService(
        CampaignEngineDbContext dbContext,
        IAppLogger<PlaceholderManifestService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlaceholderManifestEntryDto>> GetByTemplateIdAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var entries = await _dbContext.PlaceholderManifests
            .AsNoTracking()
            .Where(p => p.TemplateId == templateId)
            .OrderBy(p => p.Key)
            .ToListAsync(cancellationToken);

        return entries.Select(MapToDto).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<PlaceholderManifestEntryDto> AddEntryAsync(
        Guid templateId,
        UpsertPlaceholderManifestRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTemplateExistsAsync(templateId, cancellationToken);
        await EnsureKeyUniqueAsync(templateId, request.Key, null, cancellationToken);

        var entry = CreateEntry(templateId, request);

        _dbContext.PlaceholderManifests.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Placeholder manifest entry added: TemplateId={TemplateId}, Key={Key}, Type={Type}",
            templateId, entry.Key, entry.Type);

        return MapToDto(entry);
    }

    /// <inheritdoc />
    public async Task<PlaceholderManifestEntryDto> UpdateEntryAsync(
        Guid templateId,
        Guid entryId,
        UpsertPlaceholderManifestRequest request,
        CancellationToken cancellationToken = default)
    {
        var entry = await _dbContext.PlaceholderManifests
            .FirstOrDefaultAsync(p => p.Id == entryId && p.TemplateId == templateId, cancellationToken);

        if (entry is null)
            throw new NotFoundException(nameof(PlaceholderManifestEntry), entryId);

        await EnsureKeyUniqueAsync(templateId, request.Key, entryId, cancellationToken);

        entry.Key = request.Key;
        entry.Type = request.Type;
        entry.IsFromDataSource = DeriveIsFromDataSource(request);
        entry.Description = request.Description;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Placeholder manifest entry updated: EntryId={EntryId}, TemplateId={TemplateId}, Key={Key}",
            entryId, templateId, entry.Key);

        return MapToDto(entry);
    }

    /// <inheritdoc />
    public async Task DeleteEntryAsync(
        Guid templateId,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        var entry = await _dbContext.PlaceholderManifests
            .FirstOrDefaultAsync(p => p.Id == entryId && p.TemplateId == templateId, cancellationToken);

        if (entry is null)
            throw new NotFoundException(nameof(PlaceholderManifestEntry), entryId);

        _dbContext.PlaceholderManifests.Remove(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Placeholder manifest entry deleted: EntryId={EntryId}, TemplateId={TemplateId}, Key={Key}",
            entryId, templateId, entry.Key);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlaceholderManifestEntryDto>> ReplaceManifestAsync(
        Guid templateId,
        IEnumerable<UpsertPlaceholderManifestRequest> entries,
        CancellationToken cancellationToken = default)
    {
        await EnsureTemplateExistsAsync(templateId, cancellationToken);

        var requestList = entries.ToList();

        // Validate uniqueness of keys within the provided set
        var duplicateKeys = requestList
            .GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateKeys.Count > 0)
        {
            throw new ValidationException(
                $"Duplicate placeholder keys in manifest: {string.Join(", ", duplicateKeys)}.");
        }

        // Remove existing entries
        var existing = await _dbContext.PlaceholderManifests
            .Where(p => p.TemplateId == templateId)
            .ToListAsync(cancellationToken);

        _dbContext.PlaceholderManifests.RemoveRange(existing);

        // Add new entries
        var newEntries = requestList.Select(r => CreateEntry(templateId, r)).ToList();
        _dbContext.PlaceholderManifests.AddRange(newEntries);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Placeholder manifest replaced for TemplateId={TemplateId}. {Count} entries saved.",
            templateId, newEntries.Count);

        return newEntries.OrderBy(e => e.Key).Select(MapToDto).ToList().AsReadOnly();
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private async Task EnsureTemplateExistsAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Templates.AnyAsync(t => t.Id == templateId, cancellationToken);
        if (!exists)
            throw new NotFoundException(nameof(Template), templateId);
    }

    private async Task EnsureKeyUniqueAsync(
        Guid templateId,
        string key,
        Guid? excludeEntryId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.PlaceholderManifests
            .Where(p => p.TemplateId == templateId && p.Key == key);

        if (excludeEntryId.HasValue)
            query = query.Where(p => p.Id != excludeEntryId.Value);

        var exists = await query.AnyAsync(cancellationToken);
        if (exists)
        {
            throw new ValidationException(
                $"A placeholder with key '{key}' is already declared in this template's manifest.");
        }
    }

    private static PlaceholderManifestEntry CreateEntry(
        Guid templateId,
        UpsertPlaceholderManifestRequest request)
    {
        return new PlaceholderManifestEntry
        {
            TemplateId = templateId,
            Key = request.Key,
            Type = request.Type,
            IsFromDataSource = DeriveIsFromDataSource(request),
            Description = request.Description
        };
    }

    /// <summary>
    /// FreeField type always implies operator input (IsFromDataSource = false).
    /// For other types, respect the request value.
    /// </summary>
    private static bool DeriveIsFromDataSource(UpsertPlaceholderManifestRequest request)
    {
        if (request.Type == Domain.Enums.PlaceholderType.FreeField)
            return false;

        return request.IsFromDataSource;
    }

    private static PlaceholderManifestEntryDto MapToDto(PlaceholderManifestEntry entry) => new()
    {
        Id = entry.Id,
        TemplateId = entry.TemplateId,
        Key = entry.Key,
        Type = entry.Type.ToString(),
        IsFromDataSource = entry.IsFromDataSource,
        Description = entry.Description,
        CreatedAt = entry.CreatedAt,
        UpdatedAt = entry.UpdatedAt
    };
}
