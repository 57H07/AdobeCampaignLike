using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Orchestrates a single send attempt with full SEND_LOG lifecycle logging
/// and automatic retry on transient failures.
///
/// Wraps IChannelDispatcher calls to ensure every attempt is recorded:
///   1. Log Pending  — before first dispatch attempt
///   2. Dispatch     — via the channel-specific IChannelDispatcher
///   3. Log Sent     — on success
///   4. Log Retrying — on transient failure (with retry count increment)
///   5. Log Failed   — on permanent failure or after all retries exhausted
///
/// Retry policy (US-035):
///   - Up to 3 retry attempts for transient failures
///   - Exponential backoff: 30s / 2min / 10min
///   - Retry count tracked in SendLog (RetryCount field)
///   - Permanent failures are never retried
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
    private readonly IRetryPolicy _retryPolicy;
    private readonly IAppLogger<LoggingDispatchOrchestrator> _logger;

    public LoggingDispatchOrchestrator(
        IChannelDispatcherRegistry registry,
        ISendLogService sendLogService,
        IRetryPolicy retryPolicy,
        IAppLogger<LoggingDispatchOrchestrator> logger)
    {
        _registry = registry;
        _sendLogService = sendLogService;
        _retryPolicy = retryPolicy;
        _logger = logger;
    }

    /// <summary>
    /// Sends a message, logging the attempt to SEND_LOG before and after dispatch.
    /// Automatically retries on transient failures with exponential backoff.
    /// Returns the SendLog ID and the final dispatch result.
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

        // Step 2: Execute with retry policy
        var result = await _retryPolicy.ExecuteAsync(
            operation: async (retryAttempt, ct) =>
            {
                var attemptCount = currentRetryCount + retryAttempt;

                try
                {
                    if (!_registry.HasDispatcher(request.Channel))
                    {
                        return DispatchResult.Fail(
                            $"No dispatcher registered for channel '{request.Channel}'.",
                            isTransient: false);
                    }

                    var dispatcher = _registry.GetDispatcher(request.Channel);
                    return await dispatcher.SendAsync(request, ct);
                }
                catch (Exception ex)
                {
                    // Unexpected exception — treat as transient failure
                    var errorMsg = $"Unhandled exception during dispatch: {ex.Message}";

                    _logger.LogError(ex,
                        "Unhandled exception during dispatch — SendLogId={SendLogId} Channel={Channel} Recipient={Recipient} Attempt={Attempt}",
                        sendLogId, request.Channel, recipientAddress, attemptCount + 1);

                    return DispatchResult.Fail(errorMsg, isTransient: true);
                }
            },
            onRetry: async (failedResult, retryAttemptNumber, delay) =>
            {
                var retryCount = currentRetryCount + retryAttemptNumber;

                _logger.LogWarning(
                    "Transient dispatch failure — SendLogId={SendLogId} Attempt={Attempt}/{Max} " +
                    "RetryIn={DelaySeconds}s Error={Error}",
                    sendLogId, retryAttemptNumber, _retryPolicy.MaxAttempts,
                    delay.TotalSeconds, failedResult.ErrorDetail ?? "unknown");

                // Update SendLog with Retrying status and current retry count (TASK-035-04)
                await _sendLogService.LogRetryingAsync(
                    sendLogId,
                    failedResult.ErrorDetail ?? "Transient failure — retrying",
                    retryCount,
                    cancellationToken);
            },
            cancellationToken: cancellationToken);

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
            // Transient failure after all retries exhausted — mark as Failed
            // (RetryPolicy.ExecuteAsync has already called onRetry for each attempt)
            await _sendLogService.LogFailedAsync(
                sendLogId,
                result.ErrorDetail ?? "Transient failure — max retries exhausted",
                _retryPolicy.MaxAttempts,
                cancellationToken);
        }
        else
        {
            // Permanent failure — log with current retry count
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
