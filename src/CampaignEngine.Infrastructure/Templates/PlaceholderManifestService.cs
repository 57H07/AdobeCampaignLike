using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Exceptions;
using Mapster;

namespace CampaignEngine.Infrastructure.Templates;

/// <summary>
/// Infrastructure implementation of IPlaceholderManifestService.
/// Persists placeholder manifest entries for templates via the repository pattern.
/// Business rules enforced here:
///   - Keys must be unique within a template's manifest.
///   - FreeField placeholders default IsFromDataSource to false.
///   - Template must exist before adding manifest entries.
/// </summary>
public sealed class PlaceholderManifestService : IPlaceholderManifestService
{
    private readonly IPlaceholderManifestRepository _manifestRepository;
    private readonly ITemplateRepository _templateRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppLogger<PlaceholderManifestService> _logger;

    public PlaceholderManifestService(
        IPlaceholderManifestRepository manifestRepository,
        ITemplateRepository templateRepository,
        IUnitOfWork unitOfWork,
        IAppLogger<PlaceholderManifestService> logger)
    {
        _manifestRepository = manifestRepository;
        _templateRepository = templateRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlaceholderManifestEntryDto>> GetByTemplateIdAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var entries = await _manifestRepository.GetByTemplateIdAsync(templateId, noTracking: true, cancellationToken);
        return entries.Select(e => e.Adapt<PlaceholderManifestEntryDto>()).ToList().AsReadOnly();
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

        await _manifestRepository.AddAsync(entry, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Placeholder manifest entry added: TemplateId={TemplateId}, Key={Key}, Type={Type}",
            templateId, entry.Key, entry.Type);

        return entry.Adapt<PlaceholderManifestEntryDto>();
    }

    /// <inheritdoc />
    public async Task<PlaceholderManifestEntryDto> UpdateEntryAsync(
        Guid templateId,
        Guid entryId,
        UpsertPlaceholderManifestRequest request,
        CancellationToken cancellationToken = default)
    {
        var entry = await _manifestRepository.GetByIdAndTemplateIdAsync(entryId, templateId, cancellationToken);

        if (entry is null)
            throw new NotFoundException(nameof(PlaceholderManifestEntry), entryId);

        await EnsureKeyUniqueAsync(templateId, request.Key, entryId, cancellationToken);

        entry.Key = request.Key;
        entry.Type = request.Type;
        entry.IsFromDataSource = DeriveIsFromDataSource(request);
        entry.Description = request.Description;

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Placeholder manifest entry updated: EntryId={EntryId}, TemplateId={TemplateId}, Key={Key}",
            entryId, templateId, entry.Key);

        return entry.Adapt<PlaceholderManifestEntryDto>();
    }

    /// <inheritdoc />
    public async Task DeleteEntryAsync(
        Guid templateId,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        var entry = await _manifestRepository.GetByIdAndTemplateIdAsync(entryId, templateId, cancellationToken);

        if (entry is null)
            throw new NotFoundException(nameof(PlaceholderManifestEntry), entryId);

        _manifestRepository.Remove(entry);
        await _unitOfWork.CommitAsync(cancellationToken);

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
        var existing = await _manifestRepository.GetByTemplateIdAsync(templateId, noTracking: false, cancellationToken);
        _manifestRepository.RemoveRange(existing);

        // Add new entries
        var newEntries = requestList.Select(r => CreateEntry(templateId, r)).ToList();
        await _manifestRepository.AddRangeAsync(newEntries, cancellationToken);

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Placeholder manifest replaced for TemplateId={TemplateId}. {Count} entries saved.",
            templateId, newEntries.Count);

        return newEntries.OrderBy(e => e.Key).Select(e => e.Adapt<PlaceholderManifestEntryDto>()).ToList().AsReadOnly();
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private async Task EnsureTemplateExistsAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var template = await _templateRepository.GetByIdNoTrackingAsync(templateId, cancellationToken);
        if (template is null)
            throw new NotFoundException(nameof(Template), templateId);
    }

    private async Task EnsureKeyUniqueAsync(
        Guid templateId,
        string key,
        Guid? excludeEntryId,
        CancellationToken cancellationToken)
    {
        var exists = await _manifestRepository.KeyExistsAsync(templateId, key, excludeEntryId, cancellationToken);
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
}
