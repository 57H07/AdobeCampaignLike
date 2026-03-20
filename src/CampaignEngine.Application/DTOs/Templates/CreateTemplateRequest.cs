using CampaignEngine.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// Request DTO for POST /api/templates.
/// </summary>
public class CreateTemplateRequest
{
    /// <summary>
    /// Unique display name for this template within its channel.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The communication channel this template targets.
    /// </summary>
    [Required]
    public ChannelType Channel { get; set; }

    /// <summary>
    /// The HTML body of the template. May contain Scriban placeholder syntax.
    /// </summary>
    [Required]
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary>
    /// Optional human-readable description of the template's purpose.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// When true, marks this template as a reusable sub-template block
    /// that can be embedded in parent templates via {{> name}} syntax.
    /// </summary>
    public bool IsSubTemplate { get; set; } = false;
}
