namespace CampaignEngine.Application.DTOs.DataSources;

/// <summary>
/// Describes a field in a data source schema.
/// </summary>
public class FieldDefinitionDto
{
    public string FieldName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public bool IsFilterable { get; set; }
}
