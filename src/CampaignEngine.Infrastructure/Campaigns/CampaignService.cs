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
    private readonly ICampaignRepository _campaignRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITemplateSnapshotService _snapshotService;
    private readonly ICampaignStatusService _statusService;
    private readonly IAppLogger<CampaignService> _logger;

    public CampaignService(
        ICampaignRepository campaignRepository,
        IUnitOfWork unitOfWork,
        ITemplateSnapshotService snapshotService,
        ICampaignStatusService statusService,
        IAppLogger<CampaignService> logger)
    {
        _campaignRepository = campaignRepository;
        _unitOfWork = unitOfWork;
        _snapshotService = snapshotService;
        _statusService = statusService;
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
        // Validate: at least one step required (upper bound enforced by Campaign.AddStep)
        // ----------------------------------------------------------------
        if (request.Steps == null || request.Steps.Count == 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["steps"] = ["At least one campaign step is required."]
            });

        // Validate: step orders are unique
        var stepOrders = request.Steps.Select(s => s.StepOrder).OrderBy(o => o).ToList();
        var duplicateOrders = stepOrders.GroupBy(o => o).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateOrders.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["steps"] = [$"Step orders must be unique. Duplicate order(s): {string.Join(", ", duplicateOrders)}."]
            });

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

        // Add ordered steps — Campaign.AddStep enforces the 10-step maximum
        foreach (var stepRequest in request.Steps.OrderBy(s => s.StepOrder))
        {
            campaign.AddStep(new CampaignStep
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
            campaign.Id, campaign.Name, campaign.Steps.Count, createdBy ?? "unknown");

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

        var previousStatus = campaign.Status;

        // Domain entity enforces: Draft status, ScheduledAt set, ≥5 min ahead
        campaign.Schedule();

        // Create immutable template snapshots for all steps (US-025)
        await _snapshotService.CreateSnapshotsForCampaignAsync(id, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        // Log the status transition (US-027)
        await _statusService.LogTransitionAsync(
            id, previousStatus, campaign.Status, "Scheduled by operator", cancellationToken);

        _logger.LogInformation(
            "Campaign scheduled. Id={CampaignId}, ScheduledAt={ScheduledAt}",
            campaign.Id, campaign.ScheduledAt?.ToString() ?? "unknown");

        return await GetByIdAsync(id, cancellationToken)
               ?? throw new InvalidOperationException("Campaign was scheduled but could not be retrieved.");
    }

    /// <inheritdoc />
    public async Task<CampaignStatusDto?> GetStatusAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var campaign = await _campaignRepository.GetNoTrackingAsync(id, cancellationToken);
        if (campaign is null)
            return null;

        var history = await _statusService.GetHistoryAsync(id, cancellationToken);

        return new CampaignStatusDto
        {
            CampaignId = campaign.Id,
            Name = campaign.Name,
            Status = campaign.Status.ToString(),
            TotalRecipients = campaign.TotalRecipients,
            ProcessedCount = campaign.ProcessedCount,
            SuccessCount = campaign.SuccessCount,
            FailureCount = campaign.FailureCount,
            StartedAt = campaign.StartedAt,
            CompletedAt = campaign.CompletedAt,
            History = history.Select(h => new CampaignStatusTransitionEntry
            {
                FromStatus = h.FromStatus,
                ToStatus = h.ToStatus,
                Reason = h.Reason,
                OccurredAt = h.OccurredAt
            }).ToList()
        };
    }
}
