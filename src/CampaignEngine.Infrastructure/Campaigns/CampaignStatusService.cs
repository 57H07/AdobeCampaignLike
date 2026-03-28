using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;

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

    private readonly ICampaignStatusHistoryRepository _historyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppLogger<CampaignStatusService> _logger;

    public CampaignStatusService(
        ICampaignStatusHistoryRepository historyRepository,
        IUnitOfWork unitOfWork,
        IAppLogger<CampaignStatusService> logger)
    {
        _historyRepository = historyRepository;
        _unitOfWork = unitOfWork;
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

        await _historyRepository.AddAsync(entry, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Campaign {CampaignId}: status transition {From} -> {To}. Reason: {Reason}",
            campaignId, from, to, reason ?? "(none)");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CampaignStatusTransitionDto>> GetHistoryAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var entries = await _historyRepository.GetByCampaignIdAsync(campaignId, cancellationToken);

        return entries
            .Select(h => new CampaignStatusTransitionDto(
                h.Id,
                h.CampaignId,
                h.FromStatus.ToString(),
                h.ToStatus.ToString(),
                h.Reason,
                h.OccurredAt))
            .ToList();
    }
}
