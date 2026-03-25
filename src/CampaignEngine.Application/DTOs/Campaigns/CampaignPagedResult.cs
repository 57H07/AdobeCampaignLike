namespace CampaignEngine.Application.DTOs.Campaigns;

/// <summary>
/// Paginated list of campaigns returned from GET /api/campaigns.
/// </summary>
public class CampaignPagedResult
{
    /// <summary>The campaigns on the current page.</summary>
    public IReadOnlyList<CampaignDto> Items { get; init; } = Array.Empty<CampaignDto>();

    /// <summary>Total number of campaigns matching the filter (across all pages).</summary>
    public int Total { get; init; }

    /// <summary>Current page number (1-based).</summary>
    public int Page { get; init; }

    /// <summary>Page size requested.</summary>
    public int PageSize { get; init; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;
}
