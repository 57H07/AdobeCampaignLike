using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.DTOs.DataSources;

/// <summary>
/// Filter parameters for the GET /api/datasources query.
/// </summary>
public class DataSourceFilter
{
    /// <summary>Filter by data source type. Null returns all types.</summary>
    public DataSourceType? Type { get; set; }

    /// <summary>Filter by active status. Null returns both active and inactive.</summary>
    public bool? IsActive { get; set; }

    /// <summary>Partial name match (case-insensitive). Null returns all names.</summary>
    public string? NameContains { get; set; }

    /// <summary>Page number (1-based). Default 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Page size (1–100). Default 20.</summary>
    public int PageSize { get; set; } = 20;
}
