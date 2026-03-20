using System.ComponentModel.DataAnnotations;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.DTOs.DataSources;

/// <summary>
/// Request DTO for updating an existing data source.
/// If ConnectionString is null or empty, the stored encrypted string is preserved.
/// Admin role required (Business Rule BR-3).
/// </summary>
public class UpdateDataSourceRequest
{
    /// <summary>Updated display name. Must remain unique across all data sources.</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Updated connector type.</summary>
    [Required]
    public DataSourceType Type { get; set; }

    /// <summary>
    /// New plaintext connection string (will be re-encrypted).
    /// Leave null or empty to keep the existing encrypted string unchanged.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Updated description.</summary>
    [MaxLength(500)]
    public string? Description { get; set; }
}
