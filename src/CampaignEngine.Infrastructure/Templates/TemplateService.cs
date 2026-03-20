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
/// </summary>
public sealed class TemplateService : ITemplateService
{
    private readonly CampaignEngineDbContext _dbContext;
    private readonly IAppLogger<TemplateService> _logger;

    public TemplateService(
        CampaignEngineDbContext dbContext,
        IAppLogger<TemplateService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
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

        template.Name = request.Name;
        template.HtmlBody = request.HtmlBody;
        template.Description = request.Description;

        if (request.IsSubTemplate.HasValue)
            template.IsSubTemplate = request.IsSubTemplate.Value;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Template updated: Id={TemplateId}, Name={Name}", template.Id, template.Name);

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
}
