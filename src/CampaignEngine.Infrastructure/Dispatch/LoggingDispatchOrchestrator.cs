using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Orchestrates a single send attempt with full SEND_LOG lifecycle logging.
/// Wraps IChannelDispatcher calls to ensure every attempt is recorded:
///   1. Log Pending  — before dispatch
///   2. Dispatch     — via the channel-specific IChannelDispatcher
///   3. Log Sent     — on success
///   4. Log Failed   — on permanent failure
///   5. Log Retrying — on transient failure
///
/// Business rules (US-034):
///   - All sends logged before dispatch attempt (Pending)
///   - Status updated after dispatch result
///   - Error details captured in ErrorDetail field
/// </summary>
public class LoggingDispatchOrchestrator : ILoggingDispatchOrchestrator
{
    private readonly IChannelDispatcherRegistry _registry;
    private readonly ISendLogService _sendLogService;
    private readonly IAppLogger<LoggingDispatchOrchestrator> _logger;

    public LoggingDispatchOrchestrator(
        IChannelDispatcherRegistry registry,
        ISendLogService sendLogService,
        IAppLogger<LoggingDispatchOrchestrator> logger)
    {
        _registry = registry;
        _sendLogService = sendLogService;
        _logger = logger;
    }

    /// <summary>
    /// Sends a message, logging the attempt to SEND_LOG before and after dispatch.
    /// Returns the SendLog ID and the dispatch result.
    /// </summary>
    public async Task<(Guid SendLogId, DispatchResult Result)> SendWithLoggingAsync(
        DispatchRequest request,
        string? correlationId = null,
        int currentRetryCount = 0,
        CancellationToken cancellationToken = default)
    {
        // Derive recipient address from channel type
        var recipientAddress = ResolveRecipientAddress(request);

        // Step 1: Log Pending BEFORE dispatch (business rule: log before attempt)
        var sendLogId = await _sendLogService.LogPendingAsync(
            campaignId: request.CampaignId ?? Guid.Empty,
            campaignStepId: request.CampaignStepId,
            channel: request.Channel,
            recipientAddress: recipientAddress,
            recipientId: request.Recipient.ExternalRef,
            correlationId: correlationId,
            cancellationToken: cancellationToken);

        DispatchResult result;

        try
        {
            // Step 2: Dispatch via channel-specific dispatcher
            if (!_registry.HasDispatcher(request.Channel))
            {
                result = DispatchResult.Fail(
                    $"No dispatcher registered for channel '{request.Channel}'.",
                    isTransient: false);
            }
            else
            {
                var dispatcher = _registry.GetDispatcher(request.Channel);
                result = await dispatcher.SendAsync(request, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Unexpected exception — treat as transient failure
            result = DispatchResult.Fail(
                $"Unhandled exception during dispatch: {ex.Message}",
                isTransient: true);

            _logger.LogError(ex,
                "Unhandled exception during dispatch — SendLogId={SendLogId} Channel={Channel} Recipient={Recipient}",
                sendLogId, request.Channel, recipientAddress);
        }

        // Step 3: Update SEND_LOG with final result
        if (result.Success)
        {
            await _sendLogService.LogSentAsync(
                sendLogId,
                result.SentAt,
                cancellationToken);
        }
        else if (result.IsTransientFailure)
        {
            await _sendLogService.LogRetryingAsync(
                sendLogId,
                result.ErrorDetail ?? "Transient failure",
                currentRetryCount + 1,
                cancellationToken);
        }
        else
        {
            await _sendLogService.LogFailedAsync(
                sendLogId,
                result.ErrorDetail ?? "Permanent failure",
                currentRetryCount,
                cancellationToken);
        }

        return (sendLogId, result);
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    private static string ResolveRecipientAddress(DispatchRequest request)
    {
        return request.Channel switch
        {
            ChannelType.Email => request.Recipient.Email ?? string.Empty,
            ChannelType.Sms => request.Recipient.PhoneNumber ?? string.Empty,
            ChannelType.Letter => request.Recipient.DisplayName ?? request.Recipient.ExternalRef ?? string.Empty,
            _ => request.Recipient.Email ?? string.Empty
        };
    }
}
