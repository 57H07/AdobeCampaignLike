using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Represents an external data repository from which recipient data is fetched.
/// Connection strings are stored encrypted at rest.
/// </summary>
public class DataSource : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public DataSourceType Type { get; set; }

    /// <summary>
    /// Encrypted connection string. Decrypted at runtime using IDataProtector.
    /// </summary>
    public string EncryptedConnectionString { get; set; } = string.Empty;

    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<DataSourceField> Fields { get; set; } = new List<DataSourceField>();
    public ICollection<Campaign> Campaigns { get; set; } = new List<Campaign>();
}
