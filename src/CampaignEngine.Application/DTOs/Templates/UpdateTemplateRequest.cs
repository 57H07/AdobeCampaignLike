using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// Request DTO for PUT /api/templates/{id}.
/// Name and Channel cannot be changed — only BodyPath and Description.
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
    /// Updated relative path from storage root to the template body file.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string BodyPath { get; set; } = string.Empty;

    /// <summary>
    /// Updated SHA-256 hex checksum of the template body file (64 hex characters), nullable.
    /// </summary>
    [MaxLength(64)]
    public string? BodyChecksum { get; set; }

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

    /// <summary>
    /// Optional binary content of the replacement DOCX file.
    /// When provided, the service writes the file as the next version using the
    /// convention <c>templates/{templateId}/v{newVersion}.docx</c> and updates
    /// <see cref="BodyPath"/> automatically.
    /// When null, the existing <see cref="BodyPath"/> value is used as-is (no file
    /// replacement occurs — only metadata fields are updated).
    /// </summary>
    public Stream? DocxContent { get; set; }
}
