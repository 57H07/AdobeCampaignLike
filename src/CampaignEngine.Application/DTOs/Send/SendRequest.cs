using CampaignEngine.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Application.DTOs.Send;

/// <summary>
/// Request DTO for the POST /api/send endpoint.
/// Represents a single transactional message send request.
/// </summary>
public class SendRequest
{
    /// <summary>
    /// The ID of the published template to use for rendering the message.
    /// The template must have status Published.
    /// </summary>
    [Required]
    public Guid TemplateId { get; set; }

    /// <summary>
    /// The communication channel for this send.
    /// Must match the channel of the specified template.
    /// Channel: Email=1, Sms=3, Letter=2.
    /// </summary>
    [Required]
    public ChannelType Channel { get; set; }

    /// <summary>
    /// Key-value data dictionary used to resolve template placeholders.
    /// All required placeholders declared in the template manifest must be provided.
    /// Values can be any JSON type.
    /// </summary>
    [Required]
    public Dictionary<string, object?> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Recipient contact information.
    /// Email address required for Email channel; phone number required for SMS channel.
    /// </summary>
    [Required]
    public SendRecipient Recipient { get; set; } = new();
}
