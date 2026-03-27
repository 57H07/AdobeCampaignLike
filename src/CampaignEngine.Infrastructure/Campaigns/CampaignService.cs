using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using Mapster;

namespace CampaignEngine.Infrastructure.Campaigns;

/// <summary>
/// Manages campaign creation, retrieval, and scheduling.
/// Business rules enforced:
///   - Campaign name must be unique.
///   - Only Published templates can be used in steps.
///   - ScheduledAt must be at least 5 minutes in the future.
///   - Template snapshots are created atomically when scheduling (US-025).
/// </summary>
public sealed class CampaignService : ICampaignService
{
    private static readonly TimeSpan MinScheduleAhead = TimeSpan.FromMinutes(5);

    private readonly ICampaignRepository _campaignRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITemplateSnapshotService _snapshotService;
    private readonly IAppLogger<CampaignService> _logger;

    public CampaignService(
        ICampaignRepository campaignRepository,
        IUnitOfWork unitOfWork,
        ITemplateSnapshotService snapshotService,
        IAppLogger<CampaignService> logger)
    {
        _campaignRepository = campaignRepository;
        _unitOfWork = unitOfWork;
        _snapshotService = snapshotService;
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
        var nameExists = await _campaignRepository.ExistsWithNameAsync(request.Name, cancellationToken);

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
        var templates = await _campaignRepository.GetTemplateValidationsAsync(templateIds, cancellationToken);

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
            var dsExists = await _campaignRepository.DataSourceExistsAsync(
                request.DataSourceId.Value, cancellationToken);

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

        await _campaignRepository.AddAsync(campaign, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

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
        return await _campaignRepository.GetPagedAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CampaignDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var campaign = await _campaignRepository.GetWithDetailsAsync(id, cancellationToken);
        return campaign is null ? null : campaign.Adapt<CampaignDto>();
    }

    /// <inheritdoc />
    public async Task<CampaignDto> ScheduleAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var campaign = await _campaignRepository.GetWithStepsAsync(id, cancellationToken);

        if (campaign is null)
            throw new NotFoundException("Campaign", id);

        if (campaign.Status != CampaignStatus.Draft)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = [$"Campaign must be in Draft status to schedule. Current: {campaign.Status}."]
            });

        if (!campaign.ScheduledAt.HasValue)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["scheduledAt"] = ["Campaign ScheduledAt must be set before scheduling."]
            });

        var minSchedule = DateTime.UtcNow.Add(MinScheduleAhead);
        if (campaign.ScheduledAt.Value < minSchedule)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["scheduledAt"] = [$"Scheduled date must be at least 5 minutes in the future (minimum: {minSchedule:yyyy-MM-dd HH:mm} UTC)."]
            });

        // Create immutable template snapshots for all steps (US-025)
        await _snapshotService.CreateSnapshotsForCampaignAsync(id, cancellationToken);

        campaign.Status = CampaignStatus.Scheduled;
        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Campaign scheduled. Id={CampaignId}, ScheduledAt={ScheduledAt}",
            campaign.Id, campaign.ScheduledAt);

        return await GetByIdAsync(id, cancellationToken)
               ?? throw new InvalidOperationException("Campaign was scheduled but could not be retrieved.");
    }
}
