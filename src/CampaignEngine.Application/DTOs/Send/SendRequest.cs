using CampaignEngine.Domain.Enums;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Application.DTOs.Send;

/// <summary>
/// Request DTO for the POST /api/send endpoint.
/// Represents a single transactional message send request.
/// </summary>
[SwaggerSchema(Description = "Single transactional send request. Template must be in Published status.")]
public class SendRequest
{
    /// <summary>
    /// The ID of the published template to use for rendering the message.
    /// The template must have status Published.
    /// </summary>
    [Required]
    [SwaggerSchema(Description = "Published template ID.", Format = "uuid")]
    public Guid TemplateId { get; set; }

    /// <summary>
    /// The communication channel for this send.
    /// Must match the channel of the specified template.
    /// </summary>
    [Required]
    [SwaggerSchema(Description = "Channel: Email=1, Sms=3, Letter=2.")]
    public ChannelType Channel { get; set; }

    /// <summary>
    /// Key-value data dictionary used to resolve template placeholders.
    /// All required placeholders declared in the template manifest must be provided.
    /// </summary>
    [Required]
    [SwaggerSchema(Description = "Placeholder values keyed by placeholder name. Values can be any JSON type.")]
    public Dictionary<string, object?> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Recipient contact information.
    /// Email address required for Email channel; phone number required for SMS channel.
    /// </summary>
    [Required]
    [SwaggerSchema(Description = "Recipient contact details.")]
    public SendRecipient Recipient { get; set; } = new();
}
