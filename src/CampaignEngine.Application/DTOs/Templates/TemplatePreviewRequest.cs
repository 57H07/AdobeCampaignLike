namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// Request body for POST /api/templates/{id}/preview.
/// Specifies the data source from which to fetch sample rows for rendering.
/// </summary>
public class TemplatePreviewRequest
{
    /// <summary>
    /// ID of the data source to fetch sample rows from.
    /// Must be an active data source accessible to the system.
    /// </summary>
    public Guid DataSourceId { get; set; }

    /// <summary>
    /// Number of sample rows to fetch from the data source.
    /// Business rule: maximum 5 rows (first N rows are returned).
    /// Defaults to 5 when not specified.
    /// </summary>
    public int SampleRowCount { get; set; } = 5;

    /// <summary>
    /// Index of the sample row (0-based) to use for rendering.
    /// Defaults to 0 (first row).
    /// </summary>
    public int RowIndex { get; set; } = 0;
}
