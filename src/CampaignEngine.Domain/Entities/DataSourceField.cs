using CampaignEngine.Domain.Common;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Defines a field in a data source schema, including type and filterability metadata.
/// </summary>
public class DataSourceField : AuditableEntity
{
    public Guid DataSourceId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsFilterable { get; set; } = true;
    public bool IsRecipientAddress { get; set; } = false;
    public string? Description { get; set; }

    // Navigation property
    public DataSource? DataSource { get; set; }
}
