namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// SMS provider configuration options.
/// Bound from appsettings.json section "Sms".
///
/// TASK-020-02: SMS provider configuration model.
/// Supports a generic HTTP provider or Twilio-compatible API.
///
/// Business rules:
///   BR-1: Phone numbers must be in E.164 format (+1234567890)
///   BR-2: Message truncation configurable, default 160 characters (GSM-7 single frame)
///   BR-3: Rate limiting per provider contract (default: 10/sec)
///   BR-4: Invalid numbers logged but don't fail campaign
/// </summary>
public class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>
    /// The SMS provider type. Supported values: "Generic" (HTTP POST), "Twilio".
    /// Defaults to "Generic".
    /// </summary>
    public string Provider { get; set; } = "Generic";

    /// <summary>
    /// Provider REST API base URL.
    /// For Twilio: https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages.json
    /// For generic: the full endpoint URL for the SMS send request.
    /// </summary>
    public string ProviderApiUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key or auth token for authentication.
    /// For Twilio: the Auth Token. Combined with AccountSid for Basic Auth.
    /// For generic: used as Bearer token or X-API-Key header (see ApiKeyHeaderName).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Account SID (Twilio-specific). Used as the username in HTTP Basic Auth.
    /// Ignored for generic providers.
    /// </summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>
    /// HTTP header name used to pass the API key for generic providers.
    /// Common values: "X-API-Key", "Authorization".
    /// Defaults to "X-API-Key".
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = "X-API-Key";

    /// <summary>
    /// Default sender ID (phone number or alphanumeric sender name).
    /// E.g. "+12025551234" or "MYCOMPANY".
    /// </summary>
    public string DefaultSenderId { get; set; } = string.Empty;

    /// <summary>
    /// Maximum character length per SMS message.
    /// Business rule BR-2: default 160 (GSM-7 single frame).
    /// Configurable per provider (some providers support 1600-character long messages).
    /// </summary>
    public int MaxMessageLength { get; set; } = 160;

    /// <summary>
    /// HTTP request timeout in seconds for each provider API call.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to validate phone numbers against E.164 format before sending.
    /// Business rule BR-1: E.164 required.
    /// </summary>
    public bool ValidatePhoneNumbers { get; set; } = true;

    /// <summary>
    /// Whether to request delivery receipts / status callbacks from the provider.
    /// Only supported when the provider offers a callback URL feature.
    /// </summary>
    public bool RequestDeliveryReceipts { get; set; } = false;

    /// <summary>
    /// Public URL to which the provider should POST delivery status updates.
    /// Required when RequestDeliveryReceipts is true.
    /// E.g. https://yourapp.example.com/api/sms/delivery-status
    /// </summary>
    public string? DeliveryStatusCallbackUrl { get; set; }

    /// <summary>
    /// Whether the SMS channel is enabled for sending.
    /// When false, all send attempts return a permanent failure without hitting the provider.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
