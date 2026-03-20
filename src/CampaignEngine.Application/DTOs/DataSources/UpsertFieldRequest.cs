using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Application.DTOs.DataSources;

/// <summary>
/// Describes a single field in a data source schema for create or update operations.
/// </summary>
public class UpsertFieldRequest
{
    /// <summary>The field name as it appears in the data source (e.g., column name).</summary>
    [Required, MaxLength(200)]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Data type of the field (e.g., "nvarchar", "int", "datetime", "bit").
    /// Used for filter type validation.
    /// </summary>
    [Required, MaxLength(50)]
    public string DataType { get; set; } = string.Empty;

    /// <summary>Whether this field can be used in filter expressions.</summary>
    public bool IsFilterable { get; set; } = true;

    /// <summary>Whether this field contains a recipient address (email or phone).</summary>
    public bool IsRecipientAddress { get; set; } = false;

    /// <summary>Optional human-readable description or display label.</summary>
    [MaxLength(500)]
    public string? Description { get; set; }
}
