using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using Mapster;

namespace CampaignEngine.Infrastructure.Templates;

/// <summary>
/// Infrastructure implementation of ITemplateService.
/// Persists templates to SQL Server via EF Core through ITemplateRepository.
/// Business rules enforced here:
///   - Template name must be unique within the same channel.
///   - Soft delete sets IsDeleted flag; record is preserved for audit.
///   - Status lifecycle: Draft → Published → Archived (one-way, no reversal).
/// </summary>
public sealed class TemplateService : ITemplateService
{
    private readonly ITemplateRepository _templateRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppLogger<TemplateService> _logger;
    private readonly IPlaceholderManifestService _manifestService;
    private readonly IPlaceholderParserService _parserService;

    public TemplateService(
        ITemplateRepository templateRepository,
        IUnitOfWork unitOfWork,
        IAppLogger<TemplateService> logger,
        IPlaceholderManifestService manifestService,
        IPlaceholderParserService parserService)
    {
        _templateRepository = templateRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _manifestService = manifestService;
        _parserService = parserService;
    }

    /// <inheritdoc />
    public async Task<TemplatePagedResult> GetPagedAsync(
        ChannelType? channel,
        TemplateStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (items, total) = await _templateRepository.GetPagedAsync(
            channel, status, page, pageSize, cancellationToken);

        return new TemplatePagedResult
        {
            Items = items.Select(t => t.Adapt<TemplateDto>()).ToList().AsReadOnly(),
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)total / pageSize)
        };
    }

    /// <inheritdoc />
    public async Task<Template?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _templateRepository.GetByIdNoTrackingAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Template> CreateAsync(
        CreateTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureNameUniqueAsync(request.Name, request.Channel, null, cancellationToken);

        var template = new Template
        {
            Name = request.Name,
            Channel = request.Channel,
            HtmlBody = request.HtmlBody,
            Description = request.Description,
            IsSubTemplate = request.IsSubTemplate,
            Status = TemplateStatus.Draft,
            Version = 1
        };

        await _templateRepository.AddAsync(template, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Template created: Id={TemplateId}, Name={Name}, Channel={Channel}",
            template.Id, template.Name, template.Channel);

        return template;
    }

    /// <inheritdoc />
    public async Task<Template> UpdateAsync(
        Guid id,
        UpdateTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateRepository.GetTrackedAsync(id, cancellationToken);

        if (template is null)
            throw new NotFoundException(nameof(Template), id);

        await EnsureNameUniqueAsync(request.Name, template.Channel, id, cancellationToken);

        // Business rule (US-008): snapshot current state before applying changes.
        // Version history is never deleted (audit requirement).
        var snapshot = new TemplateHistory
        {
            TemplateId = template.Id,
            Version = template.Version,
            Name = template.Name,
            HtmlBody = template.HtmlBody,
            Channel = template.Channel,
            ChangedBy = request.ChangedBy
        };
        await _templateRepository.AddHistoryAsync(snapshot, cancellationToken);

        // Apply changes and increment version
        template.Name = request.Name;
        template.HtmlBody = request.HtmlBody;
        template.Description = request.Description;
        template.Version++;

        if (request.IsSubTemplate.HasValue)
            template.IsSubTemplate = request.IsSubTemplate.Value;

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Template updated: Id={TemplateId}, Name={Name}, Version={Version}",
            template.Id, template.Name, template.Version);

        return template;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _templateRepository.GetTrackedAsync(id, cancellationToken);

        if (template is null)
            throw new NotFoundException(nameof(Template), id);

        template.IsDeleted = true;
        template.DeletedAt = DateTime.UtcNow;

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Template soft-deleted: Id={TemplateId}, Name={Name}", template.Id, template.Name);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateSummaryDto>> GetSubTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        var items = await _templateRepository.GetSubTemplatesAsync(cancellationToken);
        return items.Select(t => t.Adapt<TemplateSummaryDto>()).ToList().AsReadOnly();
    }

    // ----------------------------------------------------------------
    // Status Transition: Publish
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<Template> PublishAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _templateRepository.GetTrackedAsync(id, cancellationToken);

        if (template is null)
            throw new NotFoundException(nameof(Template), id);

        // Business rule: only Draft templates can be published
        if (template.Status != TemplateStatus.Draft)
        {
            throw new ValidationException(
                $"Template '{template.Name}' cannot be published: current status is '{template.Status}'. " +
                "Only Draft templates can be published.");
        }

        // Business rule: manifest must be complete before publishing
        var manifestEntries = await _manifestService.GetByTemplateIdAsync(id, cancellationToken);
        var validationResult = _parserService.ValidateManifestCompleteness(template.HtmlBody, manifestEntries);

        if (!validationResult.IsComplete)
        {
            var missing = string.Join(", ", validationResult.UndeclaredKeys.Select(k => $"'{k}'"));
            throw new ValidationException(
                $"Template '{template.Name}' cannot be published: placeholder manifest is incomplete. " +
                $"Undeclared placeholders: {missing}. " +
                "All placeholders used in the template HTML must be declared in the manifest before publishing.");
        }

        template.Status = TemplateStatus.Published;

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Template published: Id={TemplateId}, Name={Name}", template.Id, template.Name);

        return template;
    }

    // ----------------------------------------------------------------
    // Status Transition: Archive
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<Template> ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _templateRepository.GetTrackedAsync(id, cancellationToken);

        if (template is null)
            throw new NotFoundException(nameof(Template), id);

        // Business rule: Archived templates cannot transition anywhere
        if (template.Status == TemplateStatus.Archived)
        {
            throw new ValidationException(
                $"Template '{template.Name}' is already Archived. Archived templates cannot change status.");
        }

        template.Status = TemplateStatus.Archived;

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Template archived: Id={TemplateId}, Name={Name}", template.Id, template.Name);

        return template;
    }

    // ----------------------------------------------------------------
    // Versioning: History, Diff, Revert (US-008)
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateHistoryDto>> GetHistoryAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Ensure template exists (including soft-deleted — history is always accessible)
        var exists = await _templateRepository.ExistsIncludingDeletedAsync(id, cancellationToken);

        if (!exists)
            throw new NotFoundException(nameof(Template), id);

        var history = await _templateRepository.GetHistoryAsync(id, cancellationToken);

        return history.Select(h => h.Adapt<TemplateHistoryDto>()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<TemplateDiffDto> GetDiffAsync(
        Guid id,
        int fromVersion,
        int? toVersion,
        CancellationToken cancellationToken = default)
    {
        // Get template (fromVersion and toVersion must exist)
        var template = await _templateRepository.GetIgnoreQueryFiltersAsync(id, cancellationToken);

        if (template is null)
            throw new NotFoundException(nameof(Template), id);

        // Resolve 'from' snapshot
        var fromEntry = await _templateRepository.GetHistoryEntryAsync(id, fromVersion, cancellationToken);

        if (fromEntry is null)
            throw new NotFoundException($"Version {fromVersion} of template", id);

        // Resolve 'to': use specified version snapshot, or current live version
        string toHtmlBody;
        string toName;
        int resolvedToVersion;

        if (toVersion.HasValue)
        {
            if (toVersion.Value == template.Version)
            {
                // 'to' is the current live version
                toHtmlBody = template.HtmlBody;
                toName = template.Name;
                resolvedToVersion = template.Version;
            }
            else
            {
                var toEntry = await _templateRepository.GetHistoryEntryAsync(
                    id, toVersion.Value, cancellationToken);

                if (toEntry is null)
                    throw new NotFoundException($"Version {toVersion.Value} of template", id);

                toHtmlBody = toEntry.HtmlBody;
                toName = toEntry.Name;
                resolvedToVersion = toEntry.Version;
            }
        }
        else
        {
            // Default: compare against current live version
            toHtmlBody = template.HtmlBody;
            toName = template.Name;
            resolvedToVersion = template.Version;
        }

        return new TemplateDiffDto
        {
            TemplateId = id,
            FromVersion = fromVersion,
            ToVersion = resolvedToVersion,
            FromHtmlBody = fromEntry.HtmlBody,
            ToHtmlBody = toHtmlBody,
            FromName = fromEntry.Name,
            ToName = toName,
            NameChanged = fromEntry.Name != toName,
            HtmlBodyChanged = fromEntry.HtmlBody != toHtmlBody
        };
    }

    /// <inheritdoc />
    public async Task<Template> RevertToVersionAsync(
        Guid id,
        int version,
        string? changedBy,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateRepository.GetTrackedAsync(id, cancellationToken);

        if (template is null)
            throw new NotFoundException(nameof(Template), id);

        var historyEntry = await _templateRepository.GetHistoryEntryAsync(id, version, cancellationToken);

        if (historyEntry is null)
            throw new NotFoundException($"Version {version} of template", id);

        // Business rule: revert creates a new version — it does not overwrite history.
        // Snapshot the current state before reverting.
        var snapshot = new TemplateHistory
        {
            TemplateId = template.Id,
            Version = template.Version,
            Name = template.Name,
            HtmlBody = template.HtmlBody,
            Channel = template.Channel,
            ChangedBy = changedBy
        };
        await _templateRepository.AddHistoryAsync(snapshot, cancellationToken);

        // Apply the historic content as a new version
        template.Name = historyEntry.Name;
        template.HtmlBody = historyEntry.HtmlBody;
        template.Version++;

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Template reverted: Id={TemplateId}, RevertedToVersion={RevertedVersion}, NewVersion={NewVersion}",
            template.Id, version, template.Version);

        return template;
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private async Task EnsureNameUniqueAsync(
        string name,
        ChannelType channel,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var exists = await _templateRepository.NameExistsAsync(name, channel, excludeId, cancellationToken);

        if (exists)
        {
            throw new ValidationException(
                $"A template named '{name}' already exists for channel '{channel}'.");
        }
    }
}
