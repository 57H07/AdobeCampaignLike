using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// EF Core-backed implementation of ISendLogService.
/// Provides atomic logging of every send attempt to the SEND_LOG table.
/// Business rules:
///   - All sends logged before dispatch (Pending)
///   - Status updated after dispatch result (Sent / Failed / Retrying)
///   - Error details captured in ErrorDetail field
///   - Retention policy applied via scheduled cleanup (configurable, default 90 days)
/// </summary>
public class SendLogService : ISendLogService
{
    private readonly ISendLogRepository _sendLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppLogger<SendLogService> _logger;

    public SendLogService(
        ISendLogRepository sendLogRepository,
        IUnitOfWork unitOfWork,
        IAppLogger<SendLogService> logger)
    {
        _sendLogRepository = sendLogRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Guid> LogPendingAsync(
        Guid campaignId,
        Guid? campaignStepId,
        ChannelType channel,
        string recipientAddress,
        string? recipientId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new SendLog
        {
            CampaignId = campaignId,
            CampaignStepId = campaignStepId,
            Channel = channel,
            Status = SendStatus.Pending,
            RecipientAddress = recipientAddress,
            RecipientId = recipientId,
            CorrelationId = correlationId,
            RetryCount = 0
        };

        await _sendLogRepository.AddAsync(entry, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "SendLog [{Status}] created — Id={SendLogId} CampaignId={CampaignId} Channel={Channel} Recipient={Recipient}",
            nameof(SendStatus.Pending), entry.Id, campaignId, channel, recipientAddress);

        return entry.Id;
    }

    /// <inheritdoc />
    public async Task LogSentAsync(
        Guid sendLogId,
        DateTime sentAt,
        CancellationToken cancellationToken = default)
    {
        var entry = await FindOrThrowAsync(sendLogId, cancellationToken);

        entry.Status = SendStatus.Sent;
        entry.SentAt = sentAt;
        entry.ErrorDetail = null;

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "SendLog [{Status}] — Id={SendLogId} SentAt={SentAt}",
            nameof(SendStatus.Sent), sendLogId, sentAt);
    }

    /// <inheritdoc />
    public async Task LogFailedAsync(
        Guid sendLogId,
        string errorDetail,
        int retryCount = 0,
        CancellationToken cancellationToken = default)
    {
        var entry = await FindOrThrowAsync(sendLogId, cancellationToken);

        entry.Status = SendStatus.Failed;
        entry.ErrorDetail = errorDetail;
        entry.RetryCount = retryCount;

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogWarning(
            "SendLog [{Status}] — Id={SendLogId} RetryCount={RetryCount} Error={ErrorDetail}",
            nameof(SendStatus.Failed), sendLogId, retryCount, errorDetail);
    }

    /// <inheritdoc />
    public async Task LogRetryingAsync(
        Guid sendLogId,
        string errorDetail,
        int retryCount,
        CancellationToken cancellationToken = default)
    {
        var entry = await FindOrThrowAsync(sendLogId, cancellationToken);

        entry.Status = SendStatus.Retrying;
        entry.ErrorDetail = errorDetail;
        entry.RetryCount = retryCount;

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "SendLog [{Status}] — Id={SendLogId} RetryCount={RetryCount} Error={ErrorDetail}",
            nameof(SendStatus.Retrying), sendLogId, retryCount, errorDetail);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SendLog>> QueryAsync(
        Guid? campaignId = null,
        string? recipientAddress = null,
        SendStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return await _sendLogRepository.QueryAsync(
            campaignId, recipientAddress, status, from, to, pageNumber, pageSize, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(
        Guid? campaignId = null,
        string? recipientAddress = null,
        SendStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        return await _sendLogRepository.CountAsync(
            campaignId, recipientAddress, status, from, to, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SendLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _sendLogRepository.FindByIdTrackedAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateDeliveryStatusAsync(
        string externalMessageId,
        bool delivered,
        string? errorDetail,
        CancellationToken cancellationToken = default)
    {
        var entry = await _sendLogRepository.FindByExternalMessageIdAsync(
            externalMessageId, cancellationToken);

        if (entry is null)
        {
            _logger.LogWarning(
                "SMS delivery status callback received for unknown ExternalMessageId={ExternalMessageId}",
                externalMessageId);
            return;
        }

        if (delivered)
        {
            entry.Status = SendStatus.Sent;
            entry.SentAt ??= DateTime.UtcNow;
            entry.ErrorDetail = null;
        }
        else
        {
            entry.Status = SendStatus.Failed;
            entry.ErrorDetail = errorDetail;
        }

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "SendLog delivery status updated via callback — " +
            "Id={SendLogId} ExternalMessageId={ExternalMessageId} Delivered={Delivered}",
            entry.Id, externalMessageId, delivered);
    }

    /// <inheritdoc />
    public async Task SetExternalMessageIdAsync(
        Guid sendLogId,
        string externalMessageId,
        CancellationToken cancellationToken = default)
    {
        var entry = await FindOrThrowAsync(sendLogId, cancellationToken);
        entry.ExternalMessageId = externalMessageId;
        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogDebug(
            "SendLog ExternalMessageId set — Id={SendLogId} ExternalMessageId={ExternalMessageId}",
            sendLogId, externalMessageId);
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    private async Task<SendLog> FindOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await _sendLogRepository.FindByIdTrackedAsync(id, cancellationToken);
        if (entry is null)
            throw new InvalidOperationException($"SendLog with Id '{id}' not found.");
        return entry;
    }
}
