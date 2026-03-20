using System.ComponentModel.DataAnnotations;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.DTOs.DataSources;

/// <summary>
/// Request to test a connection using a raw (plaintext) connection string,
/// before persisting a new or updated data source.
/// </summary>
public class TestConnectionRequest
{
    /// <summary>The connector type to use for the test.</summary>
    [Required]
    public DataSourceType Type { get; set; }

    /// <summary>The plaintext connection string to test.</summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
