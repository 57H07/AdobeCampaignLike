using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Campaigns;

/// <summary>
/// Manages campaign creation and retrieval.
/// Business rules enforced:
///   - Campaign name must be unique.
///   - Only Published templates can be used in steps.
///   - ScheduledAt must be at least 5 minutes in the future.
/// </summary>
public sealed class CampaignService : ICampaignService
{
    private static readonly TimeSpan MinScheduleAhead = TimeSpan.FromMinutes(5);

    private readonly CampaignEngineDbContext _dbContext;
    private readonly IAppLogger<CampaignService> _logger;

    public CampaignService(
        CampaignEngineDbContext dbContext,
        IAppLogger<CampaignService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CampaignDto> CreateAsync(
        CreateCampaignRequest request,
        string? createdBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ----------------------------------------------------------------
        // Validate: unique name
        // ----------------------------------------------------------------
        var nameExists = await _dbContext.Campaigns
            .AnyAsync(c => c.Name == request.Name, cancellationToken);

        if (nameExists)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["name"] = [$"A campaign named '{request.Name}' already exists."]
            });

        // ----------------------------------------------------------------
        // Validate: at least one step and at most 10 steps
        // ----------------------------------------------------------------
        if (request.Steps == null || request.Steps.Count == 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["steps"] = ["At least one campaign step is required."]
            });

        if (request.Steps.Count > 10)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["steps"] = [$"A campaign may have at most 10 steps. Provided: {request.Steps.Count}."]
            });

        // Validate: step orders are unique and contiguous (1-based)
        var stepOrders = request.Steps.Select(s => s.StepOrder).OrderBy(o => o).ToList();
        var duplicateOrders = stepOrders.GroupBy(o => o).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateOrders.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["steps"] = [$"Step orders must be unique. Duplicate order(s): {string.Join(", ", duplicateOrders)}."]
            });

        // ----------------------------------------------------------------
        // Validate: ScheduledAt must be at least 5 minutes in the future
        // ----------------------------------------------------------------
        if (request.ScheduledAt.HasValue)
        {
            var minSchedule = DateTime.UtcNow.Add(MinScheduleAhead);
            if (request.ScheduledAt.Value < minSchedule)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["scheduledAt"] = [$"Scheduled date must be at least 5 minutes in the future (minimum: {minSchedule:yyyy-MM-dd HH:mm} UTC)."]
                });
        }

        // ----------------------------------------------------------------
        // Validate: only Published templates allowed
        // ----------------------------------------------------------------
        var templateIds = request.Steps.Select(s => s.TemplateId).Distinct().ToList();
        var templates = await _dbContext.Templates
            .Where(t => templateIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.Status, t.Channel })
            .ToListAsync(cancellationToken);

        // Check all templates exist
        var missingIds = templateIds.Except(templates.Select(t => t.Id)).ToList();
        if (missingIds.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["steps"] = [$"Template(s) not found: {string.Join(", ", missingIds)}."]
            });

        // Check all templates are Published
        var unpublished = templates.Where(t => t.Status != TemplateStatus.Published).ToList();
        if (unpublished.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["steps"] = [$"Only Published templates can be used in campaigns. Non-published: {string.Join(", ", unpublished.Select(t => t.Name))}."]
            });

        // ----------------------------------------------------------------
        // Validate: DataSource exists if specified
        // ----------------------------------------------------------------
        if (request.DataSourceId.HasValue)
        {
            var dsExists = await _dbContext.DataSources
                .AnyAsync(d => d.Id == request.DataSourceId.Value, cancellationToken);

            if (!dsExists)
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["dataSourceId"] = [$"Data source with ID '{request.DataSourceId.Value}' not found."]
                });
        }

        // ----------------------------------------------------------------
        // Create campaign entity
        // ----------------------------------------------------------------
        var campaign = new Campaign
        {
            Name = request.Name,
            Status = CampaignStatus.Draft,
            DataSourceId = request.DataSourceId,
            FilterExpression = request.FilterExpression,
            FreeFieldValues = request.FreeFieldValues,
            ScheduledAt = request.ScheduledAt,
            CreatedBy = createdBy
        };

        // Add ordered steps
        foreach (var stepRequest in request.Steps.OrderBy(s => s.StepOrder))
        {
            campaign.Steps.Add(new CampaignStep
            {
                CampaignId = campaign.Id,
                StepOrder = stepRequest.StepOrder,
                Channel = stepRequest.Channel,
                TemplateId = stepRequest.TemplateId,
                DelayDays = stepRequest.DelayDays,
                StepFilter = stepRequest.StepFilter
            });
        }

        _dbContext.Campaigns.Add(campaign);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Campaign created. Id={CampaignId}, Name={Name}, Steps={StepCount}, CreatedBy={CreatedBy}",
            campaign.Id, campaign.Name, campaign.Steps.Count, createdBy);

        // Reload with navigation properties for the DTO
        return await GetByIdAsync(campaign.Id, cancellationToken)
               ?? throw new InvalidOperationException("Campaign was saved but could not be retrieved.");
    }

    /// <inheritdoc />
    public async Task<CampaignPagedResult> GetPagedAsync(
        CampaignFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Campaigns
            .Include(c => c.Steps)
            .Include(c => c.DataSource)
            .AsQueryable();

        if (filter.Status.HasValue)
            query = query.Where(c => c.Status == filter.Status.Value);

        if (!string.IsNullOrWhiteSpace(filter.NameContains))
            query = query.Where(c => c.Name.Contains(filter.NameContains));

        if (filter.DataSourceId.HasValue)
            query = query.Where(c => c.DataSourceId == filter.DataSourceId.Value);

        var total = await query.CountAsync(cancellationToken);

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new CampaignPagedResult
        {
            Items = items.Select(MapToDto).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <inheritdoc />
    public async Task<CampaignDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var campaign = await _dbContext.Campaigns
            .Include(c => c.Steps)
            .Include(c => c.DataSource)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        return campaign is null ? null : MapToDto(campaign);
    }

    // ----------------------------------------------------------------
    // Private mapping helpers
    // ----------------------------------------------------------------

    private static CampaignDto MapToDto(Campaign c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Status = c.Status.ToString(),
        DataSourceId = c.DataSourceId,
        DataSourceName = c.DataSource?.Name,
        FilterExpression = c.FilterExpression,
        FreeFieldValues = c.FreeFieldValues,
        ScheduledAt = c.ScheduledAt,
        StartedAt = c.StartedAt,
        CompletedAt = c.CompletedAt,
        TotalRecipients = c.TotalRecipients,
        ProcessedCount = c.ProcessedCount,
        SuccessCount = c.SuccessCount,
        FailureCount = c.FailureCount,
        CreatedBy = c.CreatedBy,
        Steps = c.Steps
            .OrderBy(s => s.StepOrder)
            .Select(MapStepToDto)
            .ToList(),
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };

    private static CampaignStepDto MapStepToDto(CampaignStep s) => new()
    {
        Id = s.Id,
        StepOrder = s.StepOrder,
        Channel = s.Channel.ToString(),
        TemplateId = s.TemplateId,
        DelayDays = s.DelayDays,
        StepFilter = s.StepFilter,
        ScheduledAt = s.ScheduledAt,
        ExecutedAt = s.ExecutedAt
    };
}
