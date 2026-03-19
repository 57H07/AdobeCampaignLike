namespace CampaignEngine.Application.DTOs.Dispatch;

/// <summary>
/// Contact information for the message recipient.
/// </summary>
public class RecipientInfo
{
    /// <summary>
    /// Email address (used for Email channel).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Phone number in E.164 format (+1234567890) (used for SMS channel).
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Display name of the recipient.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// External reference identifier (from data source).
    /// </summary>
    public string? ExternalRef { get; set; }
}
