using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// HTTP client for sending SMS messages via an external provider REST API.
///
/// TASK-020-04: Provider API client (Twilio or generic).
///
/// Supports two provider modes (configured via SmsOptions.Provider):
///   1. "Generic" — HTTP POST with JSON body, API key via configurable header.
///   2. "Twilio"  — HTTP POST with form-encoded body, HTTP Basic Auth (AccountSid:AuthToken).
///
/// Business rules:
///   BR-2: Message is truncated to MaxMessageLength before sending (done by SmsDispatcher).
///   BR-3: Rate limiting is enforced at dispatcher level (not in this client).
///   BR-4: Invalid number errors are treated as permanent failures (HTTP 4xx from provider).
/// </summary>
public class SmsProviderClient
{
    private readonly SmsOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SmsProviderClient> _logger;

    public SmsProviderClient(
        SmsOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<SmsProviderClient> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Sends an SMS message to the given recipient phone number via the configured provider.
    /// </summary>
    /// <param name="toPhoneNumber">Destination phone number in E.164 format.</param>
    /// <param name="message">Plain text message content (already truncated).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="SmsProviderResult"/> with the provider message ID and any error information.
    /// </returns>
    /// <exception cref="SmsDispatchException">
    /// Thrown on HTTP or network errors. <see cref="SmsDispatchException.IsTransient"/> distinguishes
    /// retriable from permanent failures.
    /// </exception>
    public virtual async Task<SmsProviderResult> SendAsync(
        string toPhoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(_options.Provider, "Twilio", StringComparison.OrdinalIgnoreCase))
        {
            return await SendViaTwilioAsync(toPhoneNumber, message, cancellationToken);
        }

        return await SendViaGenericHttpAsync(toPhoneNumber, message, cancellationToken);
    }

    // ----------------------------------------------------------------
    // Twilio provider (form-encoded POST with Basic Auth)
    // ----------------------------------------------------------------

    /// <summary>
    /// Sends via Twilio REST API.
    /// POST https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages.json
    /// Authentication: HTTP Basic (AccountSid:AuthToken)
    /// Body: application/x-www-form-urlencoded (From, To, Body)
    /// </summary>
    private async Task<SmsProviderResult> SendViaTwilioAsync(
        string toPhoneNumber,
        string message,
        CancellationToken cancellationToken)
    {
        var client = CreateHttpClient();

        // Twilio Basic Auth: username = AccountSid, password = AuthToken
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.ApiKey}"));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);

        // Build form-encoded body
        var formData = new Dictionary<string, string>
        {
            ["From"] = _options.DefaultSenderId,
            ["To"]   = toPhoneNumber,
            ["Body"] = message
        };

        // If delivery receipts are requested, add StatusCallback
        if (_options.RequestDeliveryReceipts &&
            !string.IsNullOrWhiteSpace(_options.DeliveryStatusCallbackUrl))
        {
            formData["StatusCallback"] = _options.DeliveryStatusCallbackUrl;
        }

        var content = new FormUrlEncodedContent(formData);

        _logger.LogDebug(
            "Sending SMS via Twilio to {To} from {From}. Url={Url}",
            toPhoneNumber, _options.DefaultSenderId, _options.ProviderApiUrl);

        return await PostAndParseResponseAsync(client, content, cancellationToken, isTwilio: true);
    }

    // ----------------------------------------------------------------
    // Generic HTTP provider (JSON POST with API key header)
    // ----------------------------------------------------------------

    /// <summary>
    /// Sends via a generic HTTP provider.
    /// POST {ProviderApiUrl}
    /// Authentication: configurable header (default: X-API-Key)
    /// Body: application/json { "to": "...", "from": "...", "message": "..." }
    /// Response: JSON with "messageId" (or "id") field.
    /// </summary>
    private async Task<SmsProviderResult> SendViaGenericHttpAsync(
        string toPhoneNumber,
        string message,
        CancellationToken cancellationToken)
    {
        var client = CreateHttpClient();

        // API key header (configurable)
        client.DefaultRequestHeaders.Add(_options.ApiKeyHeaderName, _options.ApiKey);

        var payload = new
        {
            to      = toPhoneNumber,
            from    = _options.DefaultSenderId,
            message = message,
            // Include delivery receipt callback URL if configured
            statusCallback = _options.RequestDeliveryReceipts &&
                             !string.IsNullOrWhiteSpace(_options.DeliveryStatusCallbackUrl)
                ? _options.DeliveryStatusCallbackUrl
                : null
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug(
            "Sending SMS via Generic provider to {To} from {From}. Url={Url}",
            toPhoneNumber, _options.DefaultSenderId, _options.ProviderApiUrl);

        return await PostAndParseResponseAsync(client, content, cancellationToken, isTwilio: false);
    }

    // ----------------------------------------------------------------
    // Shared response handling
    // ----------------------------------------------------------------

    private async Task<SmsProviderResult> PostAndParseResponseAsync(
        HttpClient client,
        HttpContent content,
        CancellationToken cancellationToken,
        bool isTwilio)
    {
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(_options.ProviderApiUrl, content, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SmsDispatchException(
                $"SMS provider request timed out after {_options.TimeoutSeconds}s.",
                isTransient: true,
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SmsDispatchException(
                $"SMS provider network error: {ex.Message}",
                isTransient: true,
                innerException: ex);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            // Try to extract message ID from response
            var messageId = ExtractMessageId(responseBody, isTwilio);
            return new SmsProviderResult(messageId, statusCode, responseBody);
        }

        // Classify error as transient or permanent
        var isTransient = IsTransientHttpStatus(response.StatusCode);
        var providerError = ExtractProviderError(responseBody, isTwilio);

        _logger.LogWarning(
            "SMS provider returned HTTP {StatusCode} (transient={IsTransient}). " +
            "ProviderError={ProviderError}",
            statusCode, isTransient, providerError);

        throw new SmsDispatchException(
            $"SMS provider error (HTTP {statusCode}): {providerError ?? responseBody}",
            isTransient: isTransient,
            httpStatusCode: statusCode,
            providerErrorCode: providerError);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient("SmsProvider");
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        return client;
    }

    /// <summary>
    /// HTTP 5xx and 429 (Too Many Requests) are transient — worth retrying.
    /// HTTP 4xx (except 429) are permanent — bad request, invalid number, etc.
    /// </summary>
    private static bool IsTransientHttpStatus(HttpStatusCode status)
    {
        return status == HttpStatusCode.TooManyRequests
               || ((int)status >= 500 && (int)status < 600);
    }

    /// <summary>
    /// Attempts to extract the message ID from a successful provider response.
    /// Handles both Twilio format ({"sid": "..."}) and generic format ({"messageId": "..."} / {"id": "..."}).
    /// </summary>
    private static string? ExtractMessageId(string responseBody, bool isTwilio)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Twilio returns "sid"
            if (isTwilio && root.TryGetProperty("sid", out var sid))
                return sid.GetString();

            // Generic: try common field names
            if (root.TryGetProperty("messageId", out var msgId))
                return msgId.GetString();

            if (root.TryGetProperty("message_id", out var msgId2))
                return msgId2.GetString();

            if (root.TryGetProperty("id", out var id))
                return id.GetString();
        }
        catch (JsonException)
        {
            // Non-JSON response body — not an error for success cases
        }

        return null;
    }

    /// <summary>
    /// Extracts a human-readable error description from a failed provider response body.
    /// </summary>
    private static string? ExtractProviderError(string responseBody, bool isTwilio)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Twilio error format: {"code": 21211, "message": "...", "status": 400}
            if (isTwilio && root.TryGetProperty("message", out var twilioMsg))
            {
                var msg = twilioMsg.GetString() ?? string.Empty;
                if (root.TryGetProperty("code", out var code))
                    return $"Twilio code {code.GetInt32()}: {msg}";
                return msg;
            }

            // Generic: try "error", "message", "detail"
            foreach (var field in new[] { "error", "message", "detail", "description" })
            {
                if (root.TryGetProperty(field, out var prop))
                    return prop.GetString();
            }
        }
        catch (JsonException)
        {
            // Fall through — return raw body as error string
        }

        // Truncate raw body to avoid noise in logs
        return responseBody.Length > 200 ? responseBody[..200] : responseBody;
    }
}

/// <summary>
/// Result from a successful SMS provider API call.
/// </summary>
public record SmsProviderResult(string? MessageId, int HttpStatusCode, string? RawResponse);
