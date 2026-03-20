using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// Request DTO for PUT /api/templates/{id}.
/// Name and Channel cannot be changed — only HtmlBody and Description.
/// To rename or change channel, delete and recreate.
/// </summary>
public class UpdateTemplateRequest
{
    /// <summary>
    /// New display name for the template. Must remain unique within its channel.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Updated HTML body of the template.
    /// </summary>
    [Required]
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary>
    /// Updated human-readable description.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// When true, marks this template as a reusable sub-template block.
    /// </summary>
    public bool? IsSubTemplate { get; set; }

    /// <summary>
    /// Username of the user making the change. Recorded in version history.
    /// Optional — populated by the controller from the authenticated user identity.
    /// </summary>
    [MaxLength(200)]
    public string? ChangedBy { get; set; }
}
