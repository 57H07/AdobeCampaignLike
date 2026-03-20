using CampaignEngine.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// Request DTO for creating or updating a placeholder manifest entry.
/// Used by POST/PUT /api/templates/{id}/placeholders.
/// </summary>
public class UpsertPlaceholderManifestRequest
{
    /// <summary>
    /// Placeholder key as it appears in the template (without braces).
    /// Example: "customerName" for {{ customerName }} or "orders" for {{ #orders }}.
    /// </summary>
    [Required]
    [MaxLength(100)]
    [RegularExpression(@"^[a-zA-Z_][a-zA-Z0-9_]*$",
        ErrorMessage = "Key must start with a letter or underscore and contain only alphanumeric characters and underscores.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Type of data this placeholder represents.
    /// Scalar = simple value, Table = row collection, List = item collection, FreeField = operator input.
    /// </summary>
    [Required]
    public PlaceholderType Type { get; set; }

    /// <summary>
    /// True if the value comes from the configured data source (default).
    /// False if the operator must provide the value manually at campaign creation.
    /// FreeField type implies IsFromDataSource = false.
    /// </summary>
    public bool IsFromDataSource { get; set; } = true;

    /// <summary>
    /// Optional description explaining the purpose of this placeholder.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }
}
