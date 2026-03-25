namespace CampaignEngine.Application.DTOs.Campaigns;

/// <summary>
/// Request body for POST /api/campaigns/estimate-recipients.
/// Estimates the number of recipients that would be targeted by a campaign
/// before actually creating it.
/// </summary>
public class EstimateRecipientsRequest
{
    /// <summary>ID of the data source to query.</summary>
    public Guid DataSourceId { get; set; }

    /// <summary>
    /// JSON-serialized filter expression AST.
    /// Null = no filter (all records in data source).
    /// </summary>
    public string? FilterExpression { get; set; }
}
