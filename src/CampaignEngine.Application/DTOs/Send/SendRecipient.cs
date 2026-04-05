namespace CampaignEngine.Application.DTOs.Send;

/// <summary>
/// Recipient information for a single send request.
/// At least one of Email or PhoneNumber must be supplied depending on the channel.
/// </summary>
public class SendRecipient
{
    /// <summary>
    /// Email address (required for Email channel).
    /// Example: recipient@example.com
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Phone number in E.164 format, e.g. +33612345678 (required for SMS channel).
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Optional display name of the recipient.
    /// Example: Marie Dupont
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Optional external reference identifier from the caller's system.
    /// Example: CRM-12345
    /// </summary>
    public string? ExternalRef { get; set; }
}
