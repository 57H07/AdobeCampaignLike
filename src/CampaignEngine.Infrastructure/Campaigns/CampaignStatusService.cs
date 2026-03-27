using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Campaigns;

/// <summary>
/// Validates campaign status transitions and persists the transition history.
///
/// Allowed transitions:
///   Draft          → Scheduled
///   Scheduled      → Running
///   Running        → StepInProgress
///   StepInProgress → WaitingNext | Completed | PartialFailure | ManualReview
///   WaitingNext    → StepInProgress | Completed | PartialFailure | ManualReview
///
/// Terminal statuses (no further automatic transitions):
///   Completed, PartialFailure, ManualReview
/// </summary>
public sealed class CampaignStatusService : ICampaignStatusService
{
    private static readonly IReadOnlyDictionary<CampaignStatus, IReadOnlyList<CampaignStatus>> AllowedTransitions =
        new Dictionary<CampaignStatus, IReadOnlyList<CampaignStatus>>
        {
            [CampaignStatus.Draft]          = [CampaignStatus.Scheduled],
            [CampaignStatus.Scheduled]      = [CampaignStatus.Running],
            [CampaignStatus.Running]        = [CampaignStatus.StepInProgress],
            [CampaignStatus.StepInProgress] = [
                CampaignStatus.WaitingNext,
                CampaignStatus.Completed,
                CampaignStatus.PartialFailure,
                CampaignStatus.ManualReview
            ],
            [CampaignStatus.WaitingNext] = [
                CampaignStatus.StepInProgress,
                CampaignStatus.Completed,
                CampaignStatus.PartialFailure,
                CampaignStatus.ManualReview
            ],
            // Terminal states — no further transitions
            [CampaignStatus.Completed]     = [],
            [CampaignStatus.PartialFailure] = [],
            [CampaignStatus.ManualReview]   = [],
        };

    private static readonly HashSet<CampaignStatus> TerminalStatuses = new()
    {
        CampaignStatus.Completed,
        CampaignStatus.PartialFailure,
        CampaignStatus.ManualReview
    };

    private static readonly HashSet<CampaignStatus> ActiveStatuses = new()
    {
        CampaignStatus.Running,
        CampaignStatus.StepInProgress,
        CampaignStatus.WaitingNext
    };

    private readonly CampaignEngineDbContext _dbContext;
    private readonly IAppLogger<CampaignStatusService> _logger;

    public CampaignStatusService(
        CampaignEngineDbContext dbContext,
        IAppLogger<CampaignStatusService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsTransitionAllowed(CampaignStatus from, CampaignStatus to)
    {
        return AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    /// <inheritdoc />
    public IReadOnlyList<CampaignStatus> GetAllowedTransitions(CampaignStatus from)
    {
        return AllowedTransitions.TryGetValue(from, out var allowed)
            ? allowed
            : Array.Empty<CampaignStatus>();
    }

    /// <inheritdoc />
    public bool IsTerminal(CampaignStatus status) => TerminalStatuses.Contains(status);

    /// <inheritdoc />
    public bool IsActive(CampaignStatus status) => ActiveStatuses.Contains(status);

    /// <inheritdoc />
    public async Task LogTransitionAsync(
        Guid campaignId,
        CampaignStatus from,
        CampaignStatus to,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new CampaignStatusHistory
        {
            CampaignId = campaignId,
            FromStatus = from,
            ToStatus = to,
            Reason = reason,
            OccurredAt = DateTime.UtcNow
        };

        _dbContext.CampaignStatusHistories.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Campaign {CampaignId}: status transition {From} -> {To}. Reason: {Reason}",
            campaignId, from, to, reason ?? "(none)");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CampaignStatusTransitionDto>> GetHistoryAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var history = await _dbContext.CampaignStatusHistories
            .Where(h => h.CampaignId == campaignId)
            .OrderBy(h => h.OccurredAt)
            .Select(h => new CampaignStatusTransitionDto(
                h.Id,
                h.CampaignId,
                h.FromStatus.ToString(),
                h.ToStatus.ToString(),
                h.Reason,
                h.OccurredAt))
            .ToListAsync(cancellationToken);

        return history;
    }
}
