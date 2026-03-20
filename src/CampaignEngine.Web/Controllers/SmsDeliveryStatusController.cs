using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampaignEngine.Web.Controllers;

/// <summary>
/// Handles inbound delivery status callbacks from SMS providers.
///
/// TASK-020-05: Delivery status callback handler.
///
/// SMS providers (Twilio, generic) POST delivery receipt notifications to this endpoint
/// after a message has been delivered, failed, or is in transit.
///
/// Supported providers:
///   - Twilio: POST /api/sms/delivery-status/twilio
///     Form fields: MessageSid, MessageStatus, To, ErrorCode, ErrorMessage
///   - Generic: POST /api/sms/delivery-status/generic
///     JSON body: { "messageId": "...", "status": "...", "to": "..." }
///
/// The callback URL is configured in appsettings.json under Sms:DeliveryStatusCallbackUrl.
/// This endpoint must be publicly reachable (or whitelisted) for the provider to call it.
/// </summary>
[ApiController]
[Route("api/sms/delivery-status")]
[AllowAnonymous]  // Provider callbacks have no app-level auth — IP/signature validation is provider-specific
[Produces("application/json")]
public class SmsDeliveryStatusController : ControllerBase
{
    private readonly ISendLogService _sendLogService;
    private readonly ILogger<SmsDeliveryStatusController> _logger;

    public SmsDeliveryStatusController(
        ISendLogService sendLogService,
        ILogger<SmsDeliveryStatusController> logger)
    {
        _sendLogService = sendLogService;
        _logger = logger;
    }

    /// <summary>
    /// Receives delivery status callbacks from Twilio.
    /// </summary>
    /// <remarks>
    /// Twilio posts form data with fields:
    ///   MessageSid     — unique message identifier
    ///   MessageStatus  — delivered | failed | undelivered | sent | queued
    ///   To             — destination phone number
    ///   ErrorCode      — numeric error code (optional, present on failure)
    ///   ErrorMessage   — human-readable error (optional)
    ///
    /// Configure Twilio status callback URL to:
    ///   POST https://yourapp/api/sms/delivery-status/twilio
    /// </remarks>
    [HttpPost("twilio")]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> TwilioCallback(
        [FromForm] TwilioDeliveryStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Received Twilio delivery status callback. " +
            "MessageSid={MessageSid} Status={Status} To={To} ErrorCode={ErrorCode}",
            request.MessageSid,
            request.MessageStatus,
            request.To,
            request.ErrorCode);

        await ProcessDeliveryStatusAsync(
            messageId: request.MessageSid,
            status: MapTwilioStatus(request.MessageStatus),
            toPhoneNumber: request.To,
            errorDetail: string.IsNullOrWhiteSpace(request.ErrorCode)
                ? null
                : $"Twilio error {request.ErrorCode}: {request.ErrorMessage}",
            cancellationToken: cancellationToken);

        // Twilio expects a 200 OK (any 2xx). Empty body is fine.
        return Ok();
    }

    /// <summary>
    /// Receives delivery status callbacks from a generic SMS provider.
    /// </summary>
    /// <remarks>
    /// Generic providers post JSON with fields:
    ///   messageId — provider message identifier
    ///   status    — delivered | failed | pending | sent
    ///   to        — destination phone number
    ///   error     — error description (optional, present on failure)
    ///
    /// Configure the generic status callback URL to:
    ///   POST https://yourapp/api/sms/delivery-status/generic
    /// </remarks>
    [HttpPost("generic")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GenericCallback(
        [FromBody] GenericDeliveryStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Received generic SMS delivery status callback. " +
            "MessageId={MessageId} Status={Status} To={To}",
            request.MessageId,
            request.Status,
            request.To);

        await ProcessDeliveryStatusAsync(
            messageId: request.MessageId,
            status: MapGenericStatus(request.Status),
            toPhoneNumber: request.To,
            errorDetail: request.Error,
            cancellationToken: cancellationToken);

        return Ok();
    }

    // ----------------------------------------------------------------
    // Internal processing
    // ----------------------------------------------------------------

    /// <summary>
    /// Looks up the send log entry by provider message ID and updates its status.
    /// If no matching send log is found, logs a warning and does nothing
    /// (the provider may retry, or the message was sent outside this system).
    /// </summary>
    private async Task ProcessDeliveryStatusAsync(
        string? messageId,
        SmsDeliveryStatus status,
        string? toPhoneNumber,
        string? errorDetail,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            _logger.LogWarning(
                "SMS delivery status callback received without a message ID. " +
                "To={To} Status={Status}", toPhoneNumber, status);
            return;
        }

        try
        {
            await _sendLogService.UpdateDeliveryStatusAsync(
                messageId,
                status == SmsDeliveryStatus.Delivered,
                errorDetail,
                cancellationToken);

            _logger.LogInformation(
                "SMS delivery status updated. MessageId={MessageId} Status={Status} To={To}",
                messageId, status, toPhoneNumber);
        }
        catch (Exception ex)
        {
            // Never return a non-200 to the provider — it would trigger a flood of retries.
            // Log the error and move on.
            _logger.LogError(ex,
                "Failed to update send log for SMS delivery callback. " +
                "MessageId={MessageId} Status={Status}",
                messageId, status);
        }
    }

    // ----------------------------------------------------------------
    // Status mapping
    // ----------------------------------------------------------------

    private static SmsDeliveryStatus MapTwilioStatus(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "delivered"   => SmsDeliveryStatus.Delivered,
            "failed"      => SmsDeliveryStatus.Failed,
            "undelivered" => SmsDeliveryStatus.Failed,
            "sent"        => SmsDeliveryStatus.Sent,
            "queued"      => SmsDeliveryStatus.Sent,
            _             => SmsDeliveryStatus.Unknown
        };

    private static SmsDeliveryStatus MapGenericStatus(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "delivered" => SmsDeliveryStatus.Delivered,
            "failed"    => SmsDeliveryStatus.Failed,
            "pending"   => SmsDeliveryStatus.Sent,
            "sent"      => SmsDeliveryStatus.Sent,
            _           => SmsDeliveryStatus.Unknown
        };
}

/// <summary>Delivery status as understood by this system.</summary>
public enum SmsDeliveryStatus
{
    Unknown = 0,
    Sent = 1,
    Delivered = 2,
    Failed = 3
}

/// <summary>Form-encoded callback from Twilio.</summary>
public class TwilioDeliveryStatusRequest
{
    public string? MessageSid { get; set; }
    public string? MessageStatus { get; set; }
    public string? To { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>JSON callback from a generic SMS provider.</summary>
public class GenericDeliveryStatusRequest
{
    public string? MessageId { get; set; }
    public string? Status { get; set; }
    public string? To { get; set; }
    public string? Error { get; set; }
}
