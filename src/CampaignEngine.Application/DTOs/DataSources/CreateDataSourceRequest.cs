using System.ComponentModel.DataAnnotations;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.DTOs.DataSources;

/// <summary>
/// Request DTO for creating a new data source.
/// The connection string is passed in plaintext and encrypted at rest by the service.
/// Admin role required (Business Rule BR-3).
/// </summary>
public class CreateDataSourceRequest
{
    /// <summary>Unique display name for the data source.</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The data source connector type (SqlServer or RestApi).</summary>
    [Required]
    public DataSourceType Type { get; set; }

    /// <summary>
    /// Plaintext connection string — encrypted at rest before persistence.
    /// Never stored or returned in plaintext.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Optional human-readable description.</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Optional initial field schema. Can also be defined later via schema update.</summary>
    public IReadOnlyList<UpsertFieldRequest> Fields { get; set; } = [];
}
