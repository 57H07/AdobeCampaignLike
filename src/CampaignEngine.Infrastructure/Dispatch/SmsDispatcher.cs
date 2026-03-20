using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// IChannelDispatcher implementation for the SMS channel.
///
/// TASK-020-01: Implement SmsDispatcher.
/// TASK-020-03: Phone number validation (E.164 format).
/// TASK-020-04: Provider API client (generic HTTP or Twilio-compatible).
///
/// Business rules:
///   BR-1: Phone numbers must be in E.164 format (+1234567890).
///   BR-2: Message truncated to MaxMessageLength (default 160 chars), preserving whole words.
///   BR-3: Rate limiting is handled externally (US-022 throttler) — not in this class.
///   BR-4: Invalid phone numbers are logged but treated as permanent (non-retriable) failures.
///        They do NOT throw exceptions up the stack — the campaign continues for other recipients.
///
/// The SmsProviderClient is virtual to allow test subclasses to override HTTP behaviour
/// without a live provider.
/// </summary>
public class SmsDispatcher : IChannelDispatcher
{
    private readonly SmsOptions _smsOptions;
    private readonly SmsProviderClient _providerClient;
    private readonly ILogger<SmsDispatcher> _logger;

    public ChannelType Channel => ChannelType.Sms;

    public SmsDispatcher(
        IOptions<SmsOptions> smsOptions,
        SmsProviderClient providerClient,
        ILogger<SmsDispatcher> logger)
    {
        _smsOptions = smsOptions.Value;
        _providerClient = providerClient;
        _logger = logger;
    }

    /// <summary>
    /// Dispatches an SMS to the recipient's phone number.
    ///
    /// Steps:
    ///   1. Validate that the channel is enabled.
    ///   2. Validate recipient phone number (E.164).
    ///   3. Truncate message to MaxMessageLength (BR-2).
    ///   4. Call the SMS provider API client.
    ///   5. Return a standardised DispatchResult.
    ///
    /// Per BR-4, invalid phone numbers return a permanent failure result without throwing.
    /// </summary>
    public async Task<DispatchResult> SendAsync(
        DispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        // ----------------------------------------------------------------
        // 1. Channel enabled check
        // ----------------------------------------------------------------
        if (!_smsOptions.IsEnabled)
        {
            _logger.LogWarning("SMS channel is disabled. Skipping send for recipient.");
            return DispatchResult.Fail("SMS channel is disabled in configuration.", isTransient: false);
        }

        // ----------------------------------------------------------------
        // 2. Recipient phone number validation (BR-1, BR-4)
        // ----------------------------------------------------------------
        var phoneNumber = request.Recipient.PhoneNumber;

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            _logger.LogWarning(
                "SMS send skipped: recipient has no phone number. " +
                "CampaignId={CampaignId}",
                request.CampaignId);
            return DispatchResult.Fail(
                "Recipient phone number is required for the SMS channel.",
                isTransient: false);
        }

        if (_smsOptions.ValidatePhoneNumbers && !PhoneNumberValidator.IsValidE164(phoneNumber))
        {
            // BR-4: Invalid numbers logged but don't fail the overall campaign.
            // Return a permanent failure — the orchestrator decides whether to continue.
            _logger.LogWarning(
                "SMS send skipped: phone number '{PhoneNumber}' is not in E.164 format. " +
                "CampaignId={CampaignId}",
                phoneNumber,
                request.CampaignId);
            return DispatchResult.Fail(
                $"Phone number '{phoneNumber}' is not in E.164 format (+country_code + number). " +
                "Invalid numbers are skipped per business rule BR-4.",
                isTransient: false);
        }

        // ----------------------------------------------------------------
        // 3. Message truncation (BR-2)
        // ----------------------------------------------------------------
        var message = TruncateMessage(request.Content, _smsOptions.MaxMessageLength);

        if (message.Length < request.Content.Length)
        {
            _logger.LogInformation(
                "SMS message truncated from {Original} to {Max} characters. " +
                "CampaignId={CampaignId}",
                request.Content.Length,
                _smsOptions.MaxMessageLength,
                request.CampaignId);
        }

        // ----------------------------------------------------------------
        // 4. Send via provider
        // ----------------------------------------------------------------
        try
        {
            var result = await _providerClient.SendAsync(phoneNumber, message, cancellationToken);

            _logger.LogInformation(
                "SMS sent successfully to {PhoneNumber}. " +
                "MessageId={MessageId} CampaignId={CampaignId}",
                phoneNumber,
                result.MessageId,
                request.CampaignId);

            return DispatchResult.Ok(messageId: result.MessageId);
        }
        catch (SmsDispatchException ex)
        {
            _logger.LogError(ex,
                "SMS dispatch failed (transient={IsTransient}, httpStatus={HttpStatus}) " +
                "for {PhoneNumber}. CampaignId={CampaignId}",
                ex.IsTransient,
                ex.HttpStatusCode,
                phoneNumber,
                request.CampaignId);

            return DispatchResult.Fail(ex.Message, isTransient: ex.IsTransient);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "SMS dispatch cancelled for {PhoneNumber}. CampaignId={CampaignId}",
                phoneNumber, request.CampaignId);
            return DispatchResult.Fail("Send operation was cancelled.", isTransient: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error sending SMS to {PhoneNumber}. CampaignId={CampaignId}",
                phoneNumber, request.CampaignId);
            return DispatchResult.Fail(
                $"Unexpected error: {ex.Message}",
                isTransient: false);
        }
    }

    // ----------------------------------------------------------------
    // Message truncation (BR-2)
    // ----------------------------------------------------------------

    /// <summary>
    /// Truncates the message to at most <paramref name="maxLength"/> characters,
    /// preserving whole words at the truncation boundary where possible.
    /// Uses the same algorithm as SmsPostProcessor.TruncateWholeWords.
    /// </summary>
    internal static string TruncateMessage(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        var candidate = text[..maxLength];

        // If the next character is a word boundary, cut there
        if (maxLength >= text.Length || text[maxLength] == ' ')
            return candidate;

        // Back up to last word boundary
        var lastSpace = candidate.LastIndexOf(' ');
        return lastSpace > 0 ? candidate[..lastSpace] : candidate;
    }
}
