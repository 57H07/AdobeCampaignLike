using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.DTOs.DataSources;

/// <summary>
/// Data transfer object representing a data source definition.
/// Used to describe connectivity and schema metadata to connectors.
/// </summary>
public class DataSourceDefinitionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DataSourceType Type { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
    public IReadOnlyList<FieldDefinitionDto> Fields { get; set; } = [];
}
