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
    /// Relative path from storage root to the template body file (e.g., "templates/{id}/v1.docx").
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string BodyPath { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hex checksum of the template body file (64 hex characters), nullable.
    /// </summary>
    [MaxLength(64)]
    public string? BodyChecksum { get; set; }

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
