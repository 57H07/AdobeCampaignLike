namespace CampaignEngine.Application.DTOs.DataSources;

/// <summary>
/// Paginated result wrapper for data source list queries.
/// </summary>
public class DataSourcePagedResult
{
    public IReadOnlyList<DataSourceDto> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;
}
