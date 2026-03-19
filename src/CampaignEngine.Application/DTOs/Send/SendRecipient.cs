namespace CampaignEngine.Application.DTOs.Send;

/// <summary>
/// Recipient information for a single send request.
/// </summary>
public class SendRecipient
{
    /// <summary>
    /// Email address (required for Email channel).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Phone number in E.164 format, e.g. +33612345678 (required for SMS channel).
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Optional display name of the recipient.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Optional external reference identifier from the caller's system.
    /// </summary>
    public string? ExternalRef { get; set; }
}
