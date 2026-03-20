using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Templates;

/// <summary>
/// Infrastructure implementation of ITemplateService.
/// Persists templates to SQL Server via EF Core.
/// Business rules enforced here:
///   - Template name must be unique within the same channel.
///   - Soft delete sets IsDeleted flag; record is preserved for audit.
///   - Status lifecycle: Draft → Published → Archived (one-way, no reversal).
/// </summary>
public sealed class TemplateService : ITemplateService
{
    private readonly CampaignEngineDbContext _dbContext;
    private readonly IAppLogger<TemplateService> _logger;
    private readonly IPlaceholderManifestService _manifestService;
    private readonly IPlaceholderParserService _parserService;

    public TemplateService(
        CampaignEngineDbContext dbContext,
        IAppLogger<TemplateService> logger,
        IPlaceholderManifestService manifestService,
        IPlaceholderParserService parserService)
    {
        _dbContext = dbContext;
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
        // The global query filter on Template already excludes soft-deleted records.
        var query = _dbContext.Templates.AsNoTracking();

        if (channel.HasValue)
            query = query.Where(t => t.Channel == channel.Value);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(t => t.Channel)
            .ThenBy(t => t.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new TemplatePagedResult
        {
            Items = items.Select(MapToDto).ToList().AsReadOnly(),
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)total / pageSize)
        };
    }

    /// <inheritdoc />
    public async Task<Template?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
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

        _dbContext.Templates.Add(template);
        await _dbContext.SaveChangesAsync(cancellationToken);

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
        var template = await _dbContext.Templates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

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
        _dbContext.TemplateHistories.Add(snapshot);

        // Apply changes and increment version
        template.Name = request.Name;
        template.HtmlBody = request.HtmlBody;
        template.Description = request.Description;
        template.Version++;

        if (request.IsSubTemplate.HasValue)
            template.IsSubTemplate = request.IsSubTemplate.Value;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Template updated: Id={TemplateId}, Name={Name}, Version={Version}",
            template.Id, template.Name, template.Version);

        return template;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _dbContext.Templates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template is null)
            throw new NotFoundException(nameof(Template), id);

        template.IsDeleted = true;
        template.DeletedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Template soft-deleted: Id={TemplateId}, Name={Name}", template.Id, template.Name);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateSummaryDto>> GetSubTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.Templates
            .AsNoTracking()
            .Where(t => t.IsSubTemplate)
            .OrderBy(t => t.Channel)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

        return items.Select(t => new TemplateSummaryDto
        {
            Id = t.Id,
            Name = t.Name,
            Channel = t.Channel.ToString(),
            Status = t.Status.ToString(),
            IsSubTemplate = t.IsSubTemplate,
            Description = t.Description
        }).ToList().AsReadOnly();
    }

    // ----------------------------------------------------------------
    // Status Transition: Publish
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<Template> PublishAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _dbContext.Templates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

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

        await _dbContext.SaveChangesAsync(cancellationToken);

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
        var template = await _dbContext.Templates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template is null)
            throw new NotFoundException(nameof(Template), id);

        // Business rule: Archived templates cannot transition anywhere
        if (template.Status == TemplateStatus.Archived)
        {
            throw new ValidationException(
                $"Template '{template.Name}' is already Archived. Archived templates cannot change status.");
        }

        template.Status = TemplateStatus.Archived;

        await _dbContext.SaveChangesAsync(cancellationToken);

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
        var exists = await _dbContext.Templates
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Id == id, cancellationToken);

        if (!exists)
            throw new NotFoundException(nameof(Template), id);

        var history = await _dbContext.TemplateHistories
            .AsNoTracking()
            .Where(h => h.TemplateId == id)
            .OrderByDescending(h => h.Version)
            .ToListAsync(cancellationToken);

        return history.Select(MapHistoryToDto).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<TemplateDiffDto> GetDiffAsync(
        Guid id,
        int fromVersion,
        int? toVersion,
        CancellationToken cancellationToken = default)
    {
        // Get template (fromVersion and toVersion must exist)
        var template = await _dbContext.Templates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template is null)
            throw new NotFoundException(nameof(Template), id);

        // Resolve 'from' snapshot
        var fromEntry = await _dbContext.TemplateHistories
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.TemplateId == id && h.Version == fromVersion, cancellationToken);

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
                var toEntry = await _dbContext.TemplateHistories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(h => h.TemplateId == id && h.Version == toVersion.Value, cancellationToken);

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
        var template = await _dbContext.Templates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template is null)
            throw new NotFoundException(nameof(Template), id);

        var historyEntry = await _dbContext.TemplateHistories
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.TemplateId == id && h.Version == version, cancellationToken);

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
        _dbContext.TemplateHistories.Add(snapshot);

        // Apply the historic content as a new version
        template.Name = historyEntry.Name;
        template.HtmlBody = historyEntry.HtmlBody;
        template.Version++;

        await _dbContext.SaveChangesAsync(cancellationToken);

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
        // The global query filter excludes soft-deleted records automatically.
        var query = _dbContext.Templates
            .Where(t => t.Name == name && t.Channel == channel);

        if (excludeId.HasValue)
            query = query.Where(t => t.Id != excludeId.Value);

        var exists = await query.AnyAsync(cancellationToken);

        if (exists)
        {
            throw new ValidationException(
                $"A template named '{name}' already exists for channel '{channel}'.");
        }
    }

    private static TemplateDto MapToDto(Template t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Channel = t.Channel.ToString(),
        HtmlBody = t.HtmlBody,
        Status = t.Status.ToString(),
        Version = t.Version,
        IsSubTemplate = t.IsSubTemplate,
        Description = t.Description,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };

    private static TemplateHistoryDto MapHistoryToDto(TemplateHistory h) => new()
    {
        Id = h.Id,
        TemplateId = h.TemplateId,
        Version = h.Version,
        Name = h.Name,
        Channel = h.Channel.ToString(),
        HtmlBody = h.HtmlBody,
        ChangedBy = h.ChangedBy,
        CreatedAt = h.CreatedAt
    };
}
