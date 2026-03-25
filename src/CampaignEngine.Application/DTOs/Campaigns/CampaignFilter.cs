using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.DTOs.Campaigns;

/// <summary>
/// Filter parameters for GET /api/campaigns query.
/// </summary>
public class CampaignFilter
{
    /// <summary>Filter by campaign status. Null = all statuses.</summary>
    public CampaignStatus? Status { get; set; }

    /// <summary>Filter campaigns whose name contains this substring (case-insensitive). Null = no filter.</summary>
    public string? NameContains { get; set; }

    /// <summary>Filter by data source ID. Null = all data sources.</summary>
    public Guid? DataSourceId { get; set; }

    /// <summary>Page number (1-based, default 1).</summary>
    public int Page { get; set; } = 1;

    /// <summary>Page size (1–100, default 20).</summary>
    public int PageSize { get; set; } = 20;
}
