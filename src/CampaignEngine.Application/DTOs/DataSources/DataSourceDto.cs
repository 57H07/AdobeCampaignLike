using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.DTOs.DataSources;

/// <summary>
/// Read model representing a data source with its field schema.
/// The connection string is never exposed — only a masked indicator is included.
/// </summary>
public class DataSourceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DataSourceType Type { get; set; }
    public string TypeName => Type.ToString();
    public string? Description { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// Indicates whether a connection string has been configured (true) but never exposes it.
    /// </summary>
    public bool HasConnectionString { get; set; }

    public IReadOnlyList<DataSourceFieldDto> Fields { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Field metadata within a data source schema.
/// </summary>
public class DataSourceFieldDto
{
    public Guid Id { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsFilterable { get; set; }
    public bool IsRecipientAddress { get; set; }
    public string? Description { get; set; }
}
