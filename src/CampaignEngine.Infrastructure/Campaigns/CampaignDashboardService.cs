using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Infrastructure.Campaigns;

/// <summary>
/// Aggregates real-time progress metrics for active campaigns.
///
/// Business rules (US-036):
///   1. Returns campaigns in Running or StepInProgress status by default.
///   2. Metrics reflect current state of counters on the Campaign entity.
///   3. EstimatedCompletion is calculated from the current send rate since StartedAt.
///      Formula: if ProcessedCount > 0 and StartedAt is known,
///               rate = ProcessedCount / elapsedSeconds,
///               remaining = TotalRecipients - ProcessedCount,
///               eta = now + remaining / rate.
///   4. Supports optional filters: status, date range (startedFrom/startedTo), operator.
///   5. Step status derived from ExecutedAt / ScheduledAt:
///        - Completed: ExecutedAt is set
///        - Active: ScheduledAt is set and ExecutedAt is null and ScheduledAt <= now
///        - Waiting: ScheduledAt is set and ExecutedAt is null and ScheduledAt > now
///        - Pending: neither is set
/// </summary>
public sealed class CampaignDashboardService : ICampaignDashboardService
{
    private static readonly CampaignStatus[] DefaultActiveStatuses =
    [
        CampaignStatus.Running,
        CampaignStatus.StepInProgress
    ];

    private readonly ICampaignRepository _campaignRepository;
    private readonly IAppLogger<CampaignDashboardService> _logger;

    public CampaignDashboardService(
        ICampaignRepository campaignRepository,
        IAppLogger<CampaignDashboardService> logger)
    {
        _campaignRepository = campaignRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CampaignDashboardDto> GetDashboardAsync(
        DashboardFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Resolve statuses to query
        var statuses = ResolveStatuses(filter?.Status);

        _logger.LogInformation(
            "Dashboard query: statuses={Statuses}, startedFrom={From}, startedTo={To}, createdBy={CreatedBy}",
            string.Join(",", statuses),
            filter?.StartedFrom?.ToString("O") ?? "null",
            filter?.StartedTo?.ToString("O") ?? "null",
            filter?.CreatedBy ?? "null");

        var campaigns = await _campaignRepository.GetActiveForDashboardAsync(
            statuses,
            filter?.StartedFrom,
            filter?.StartedTo,
            filter?.CreatedBy,
            cancellationToken);

        var progressItems = campaigns
            .Select(c => MapToProgressDto(c, now))
            .ToList();

        return new CampaignDashboardDto
        {
            ComputedAtUtc = now,
            ActiveCampaignCount = progressItems.Count,
            Campaigns = progressItems
        };
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    private static IReadOnlyList<CampaignStatus> ResolveStatuses(string? statusFilter)
    {
        if (string.IsNullOrWhiteSpace(statusFilter))
            return DefaultActiveStatuses;

        // Accept comma-separated status names or integers
        var parts = statusFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<CampaignStatus>();

        foreach (var part in parts)
        {
            if (Enum.TryParse<CampaignStatus>(part, ignoreCase: true, out var parsed))
                result.Add(parsed);
            else if (int.TryParse(part, out var intVal) && Enum.IsDefined(typeof(CampaignStatus), intVal))
                result.Add((CampaignStatus)intVal);
        }

        return result.Count > 0 ? result : DefaultActiveStatuses;
    }

    private static CampaignProgressDto MapToProgressDto(Campaign campaign, DateTime now)
    {
        var eta = ComputeEstimatedCompletion(campaign, now);
        var steps = campaign.Steps
            .OrderBy(s => s.StepOrder)
            .Select(s => MapStepDto(s, now))
            .ToList();

        return new CampaignProgressDto
        {
            Id = campaign.Id,
            Name = campaign.Name,
            Status = campaign.Status.ToString(),
            CreatedBy = campaign.CreatedBy,
            TotalRecipients = campaign.TotalRecipients,
            ProcessedCount = campaign.ProcessedCount,
            SuccessCount = campaign.SuccessCount,
            FailureCount = campaign.FailureCount,
            EstimatedCompletionUtc = eta,
            StartedAt = campaign.StartedAt,
            ScheduledAt = campaign.ScheduledAt,
            Steps = steps
        };
    }

    private static DateTime? ComputeEstimatedCompletion(Campaign campaign, DateTime now)
    {
        // Cannot estimate without a start time, processed count, or if all done
        if (campaign.StartedAt is null
            || campaign.ProcessedCount <= 0
            || campaign.TotalRecipients <= 0
            || campaign.ProcessedCount >= campaign.TotalRecipients)
        {
            return null;
        }

        var elapsed = (now - campaign.StartedAt.Value).TotalSeconds;
        if (elapsed <= 0) return null;

        var ratePerSecond = campaign.ProcessedCount / elapsed;
        if (ratePerSecond <= 0) return null;

        var remaining = campaign.TotalRecipients - campaign.ProcessedCount;
        var secondsRemaining = remaining / ratePerSecond;

        return now.AddSeconds(secondsRemaining);
    }

    private static CampaignStepProgressDto MapStepDto(CampaignStep step, DateTime now)
    {
        var stepStatus = DeriveStepStatus(step, now);

        return new CampaignStepProgressDto
        {
            Id = step.Id,
            StepOrder = step.StepOrder,
            Channel = step.Channel.ToString(),
            DelayDays = step.DelayDays,
            ScheduledAt = step.ScheduledAt,
            ExecutedAt = step.ExecutedAt,
            StepStatus = stepStatus
        };
    }

    private static string DeriveStepStatus(CampaignStep step, DateTime now)
    {
        if (step.ExecutedAt.HasValue)
            return "Completed";

        if (step.ScheduledAt.HasValue)
            return step.ScheduledAt.Value <= now ? "Active" : "Waiting";

        return "Pending";
    }
}
