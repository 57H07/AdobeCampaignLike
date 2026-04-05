using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Application.Interfaces.Storage;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Templates;

/// <summary>
/// Infrastructure implementation of ITemplateService.
/// Persists templates to SQL Server via EF Core through ITemplateRepository.
/// Business rules enforced here:
///   - Template name must be unique within the same channel.
///   - Soft delete sets IsDeleted flag; record is preserved for audit.
///   - Status lifecycle: Draft → Published → Archived (one-way, no reversal).
///   - DOCX file storage: when a DocxContent stream is supplied on create/update,
///     the file is persisted via ITemplateBodyStore using the naming convention
///     templates/{templateId}/v{version}.docx (US-006).
/// </summary>
public sealed class TemplateService : ITemplateService
{
    private readonly ITemplateRepository _templateRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppLogger<TemplateService> _logger;
    private readonly IPlaceholderManifestService _manifestService;
    private readonly IPlaceholderParserService _parserService;
    private readonly ITemplateBodyStore _bodyStore;

    public TemplateService(
        ITemplateRepository templateRepository,
        IUnitOfWork unitOfWork,
        IAppLogger<TemplateService> logger,
        IPlaceholderManifestService manifestService,
        IPlaceholderParserService parserService,
        ITemplateBodyStore bodyStore)
    {
        _templateRepository = templateRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _manifestService = manifestService;
        _parserService = parserService;
        _bodyStore = bodyStore;
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

    /// <summary>Maximum allowed file size in bytes (10 MB). F-204 defense-in-depth re-validation.</summary>
    private const long MaxFileSizeBytes = 10_485_760;

    /// <inheritdoc />
    public async Task<Template> CreateAsync(
        CreateTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        // Defense-in-depth: re-validate file size even if Kestrel limit already rejected oversized requests.
        if (request.FileSizeBytes.HasValue && request.FileSizeBytes.Value > MaxFileSizeBytes)
        {
            throw new ValidationException(
                $"File size {request.FileSizeBytes.Value:N0} bytes exceeds the 10 MB limit ({MaxFileSizeBytes:N0} bytes).");
        }

        await EnsureNameUniqueAsync(request.Name, request.Channel, null, cancellationToken);

        // US-006 TASK-006-01: Build the template entity first so its ID is available
        // for DOCX path generation (ID is a client-side Guid, not a DB identity).
        const int initialVersion = 1;
        var template = new Template
        {
            Name = request.Name,
            Channel = request.Channel,
            BodyChecksum = request.BodyChecksum,
            Description = request.Description,
            IsSubTemplate = request.IsSubTemplate,
            Status = TemplateStatus.Draft,
            Version = initialVersion
        };

        // US-005 TASK-005-01/04: Atomic write pattern — file is written first, then the
        // DB transaction commits. On DB failure the newly written file is deleted synchronously
        // to prevent orphaned files. File path is tracked so cleanup knows what to remove.
        string? writtenFilePath = null;

        try
        {
            // US-006 TASK-006-01/03/04: When a DOCX stream is provided, generate the
            // conventional path and persist the file. The directory is created automatically
            // by FileSystemTemplateBodyStore.WriteAsync (TASK-006-04).
            if (request.DocxContent is not null)
            {
                var docxPath = DocxFilePathHelper.Build(template.Id, initialVersion);
                await _bodyStore.WriteAsync(docxPath, request.DocxContent, cancellationToken);
                writtenFilePath = docxPath;
                template.BodyPath = docxPath;

                _logger.LogInformation(
                    "DOCX file stored for new template: Id={TemplateId}, Path={Path}",
                    template.Id, docxPath);
            }
            // US-007 TASK-007-01: When an HTML stream is provided for Email/SMS, generate the
            // conventional path and persist the file.
            else if (request.HtmlContent is not null)
            {
                var htmlPath = HtmlFilePathHelper.Build(template.Id, initialVersion);
                await _bodyStore.WriteAsync(htmlPath, request.HtmlContent, cancellationToken);
                writtenFilePath = htmlPath;
                template.BodyPath = htmlPath;

                _logger.LogInformation(
                    "HTML file stored for new template: Id={TemplateId}, Path={Path}",
                    template.Id, htmlPath);
            }
            else
            {
                // No file stream supplied: caller supplies BodyPath directly (legacy / non-file channels).
                template.BodyPath = request.BodyPath;
            }

            await _templateRepository.AddAsync(template, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch (Exception)
        {
            // US-005 TASK-005-04: On DB commit failure, delete the newly written file
            // synchronously to avoid leaving orphaned files on disk.
            if (writtenFilePath is not null)
            {
                _logger.LogWarning(
                    "DB commit failed after file write — deleting orphaned file: {Path}",
                    writtenFilePath);

                await _bodyStore.DeleteAsync(writtenFilePath, CancellationToken.None);
            }

            throw;
        }

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
            BodyPath = template.BodyPath,
            BodyChecksum = template.BodyChecksum,
            Channel = template.Channel,
            ChangedBy = request.ChangedBy
        };
        await _templateRepository.AddHistoryAsync(snapshot, cancellationToken);

        // Apply changes and increment version
        template.Name = request.Name;
        template.Description = request.Description;
        template.Version++;

        if (request.IsSubTemplate.HasValue)
            template.IsSubTemplate = request.IsSubTemplate.Value;

        // US-005 TASK-005-02/04: Atomic update pattern — copy previous file to history first,
        // then write the new file, then commit the DB. On DB failure the newly written file
        // is deleted synchronously to prevent orphaned files.
        string? writtenFilePath = null;

        try
        {
            // US-006 TASK-006-02: When a replacement DOCX stream is provided, write it
            // under the new version number. The previous file is copied to history before writing.
            if (request.DocxContent is not null)
            {
                // US-005 TASK-005-02: Copy previous body to history/v{n}.docx (audit trail).
                if (!string.IsNullOrWhiteSpace(snapshot.BodyPath))
                {
                    var historyPath = TemplateHistoryFilePathHelper.Build(
                        template.Id, snapshot.Version, snapshot.BodyPath);
                    await _bodyStore.CopyAsync(snapshot.BodyPath, historyPath, cancellationToken);
                }

                var docxPath = DocxFilePathHelper.Build(template.Id, template.Version);
                await _bodyStore.WriteAsync(docxPath, request.DocxContent, cancellationToken);
                writtenFilePath = docxPath;
                template.BodyPath = docxPath;
                template.BodyChecksum = request.BodyChecksum;

                _logger.LogInformation(
                    "DOCX file versioned for template: Id={TemplateId}, NewVersion={Version}, Path={Path}",
                    template.Id, template.Version, docxPath);
            }
            // US-007 TASK-007-02: When a replacement HTML stream is provided for Email/SMS, write it
            // under the new version number. The previous file is copied to history before writing.
            else if (request.HtmlContent is not null)
            {
                // US-005 TASK-005-02: Copy previous body to history/v{n}.html (audit trail).
                if (!string.IsNullOrWhiteSpace(snapshot.BodyPath))
                {
                    var historyPath = TemplateHistoryFilePathHelper.Build(
                        template.Id, snapshot.Version, snapshot.BodyPath);
                    await _bodyStore.CopyAsync(snapshot.BodyPath, historyPath, cancellationToken);
                }

                var htmlPath = HtmlFilePathHelper.Build(template.Id, template.Version);
                await _bodyStore.WriteAsync(htmlPath, request.HtmlContent, cancellationToken);
                writtenFilePath = htmlPath;
                template.BodyPath = htmlPath;
                template.BodyChecksum = request.BodyChecksum;

                _logger.LogInformation(
                    "HTML file versioned for template: Id={TemplateId}, NewVersion={Version}, Path={Path}",
                    template.Id, template.Version, htmlPath);
            }
            else
            {
                // No new file supplied: keep existing path; only metadata fields were updated.
                template.BodyPath = request.BodyPath;
                template.BodyChecksum = request.BodyChecksum;
            }

            // US-005 TASK-005-02: Catch DbUpdateConcurrencyException (rowversion mismatch)
            // and map it to ConcurrencyException so GlobalExceptionMiddleware returns HTTP 409.
            try
            {
                await _unitOfWork.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ConcurrencyException(
                    $"Template '{template.Name}' was modified by another request. " +
                    "Reload the template and retry your update.",
                    ex);
            }
        }
        catch (ConcurrencyException)
        {
            // US-005 TASK-005-04: Clean up newly written file on concurrency conflict too.
            if (writtenFilePath is not null)
            {
                _logger.LogWarning(
                    "Concurrency conflict after file write — deleting orphaned file: {Path}",
                    writtenFilePath);

                await _bodyStore.DeleteAsync(writtenFilePath, CancellationToken.None);
            }

            throw;
        }
        catch (Exception) when (writtenFilePath is not null)
        {
            // US-005 TASK-005-04: On any other DB commit failure, delete the newly written file
            // synchronously to avoid leaving orphaned files on disk.
            _logger.LogWarning(
                "DB commit failed after file write — deleting orphaned file: {Path}",
                writtenFilePath);

            await _bodyStore.DeleteAsync(writtenFilePath, CancellationToken.None);
            throw;
        }

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

        // Domain entity enforces: only Draft templates can be published.
        // Check state BEFORE manifest validation so DomainException propagates for non-Draft templates
        // without requiring manifest/body-store to be set up (cleaner failure for non-Draft cases).
        if (template.Status != TemplateStatus.Draft)
            throw new DomainException(
                $"Template '{template.Name}' cannot be published: current status is '{template.Status}'. " +
                "Only Draft templates can be published.");

        // Business rule: manifest must be complete before publishing
        // US-007 TASK-007-03: Load HTML body from file store before placeholder validation.
        var manifestEntries = await _manifestService.GetByTemplateIdAsync(id, cancellationToken);
        var bodyContent = await _bodyStore.ReadAllTextAsync(template.BodyPath, cancellationToken);
        var validationResult = _parserService.ValidateManifestCompleteness(bodyContent, manifestEntries);

        if (!validationResult.IsComplete)
        {
            var missing = string.Join(", ", validationResult.UndeclaredKeys.Select(k => $"'{k}'"));
            throw new ValidationException(
                $"Template '{template.Name}' cannot be published: placeholder manifest is incomplete. " +
                $"Undeclared placeholders: {missing}. " +
                "All placeholders used in the template HTML must be declared in the manifest before publishing.");
        }

        // Transition to Published (state already validated above)
        template.Publish();

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

        // Domain entity enforces: Archived templates cannot transition anywhere
        template.Archive();

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
        string toBodyPath;
        string toName;
        int resolvedToVersion;

        if (toVersion.HasValue)
        {
            if (toVersion.Value == template.Version)
            {
                // 'to' is the current live version
                toBodyPath = template.BodyPath;
                toName = template.Name;
                resolvedToVersion = template.Version;
            }
            else
            {
                var toEntry = await _templateRepository.GetHistoryEntryAsync(
                    id, toVersion.Value, cancellationToken);

                if (toEntry is null)
                    throw new NotFoundException($"Version {toVersion.Value} of template", id);

                toBodyPath = toEntry.BodyPath;
                toName = toEntry.Name;
                resolvedToVersion = toEntry.Version;
            }
        }
        else
        {
            // Default: compare against current live version
            toBodyPath = template.BodyPath;
            toName = template.Name;
            resolvedToVersion = template.Version;
        }

        return new TemplateDiffDto
        {
            TemplateId = id,
            FromVersion = fromVersion,
            ToVersion = resolvedToVersion,
            FromBodyPath = fromEntry.BodyPath,
            ToBodyPath = toBodyPath,
            FromName = fromEntry.Name,
            ToName = toName,
            NameChanged = fromEntry.Name != toName,
            BodyPathChanged = fromEntry.BodyPath != toBodyPath
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
            BodyPath = template.BodyPath,
            BodyChecksum = template.BodyChecksum,
            Channel = template.Channel,
            ChangedBy = changedBy
        };
        await _templateRepository.AddHistoryAsync(snapshot, cancellationToken);

        // Apply the historic content as a new version
        template.Name = historyEntry.Name;
        template.BodyPath = historyEntry.BodyPath;
        template.BodyChecksum = historyEntry.BodyChecksum;
        template.Version++;

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Template reverted: Id={TemplateId}, RevertedToVersion={RevertedVersion}, NewVersion={NewVersion}",
            template.Id, version, template.Version);

        return template;
    }

    // ----------------------------------------------------------------
    // US-008: DOCX download
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<(Stream Content, string TemplateName)> GetDocxBodyAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateRepository.GetByIdNoTrackingAsync(id, cancellationToken);

        if (template is null)
            throw new NotFoundException($"Template '{id}' not found.");

        if (template.Channel != ChannelType.Letter)
            throw new DomainException(
                $"Template '{id}' is a {template.Channel} template. DOCX download is only available for Letter templates.");

        if (string.IsNullOrWhiteSpace(template.BodyPath))
            throw new DomainException(
                $"Template '{id}' does not have a DOCX file attached.");

        var stream = await _bodyStore.ReadAsync(template.BodyPath, cancellationToken);
        return (stream, template.Name);
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
