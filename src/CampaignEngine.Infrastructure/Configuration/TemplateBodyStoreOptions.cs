namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// Configuration options for <see cref="Storage.FileSystemTemplateBodyStore"/>.
///
/// US-002 TASK-002-05: Storage root path configuration.
///
/// Bound from the "CampaignEngine:Templates:Storage" section in appsettings.json.
/// Supports local absolute paths and UNC paths for network file shares.
///
/// Example appsettings.json:
/// <code>
/// "CampaignEngine": {
///   "Templates": {
///     "Storage": {
///       "RootPath": "C:\\CampaignEngine\\templates"
///     }
///   }
/// }
/// </code>
/// </summary>
public class TemplateBodyStoreOptions
{
    public const string SectionName = "CampaignEngine:Templates:Storage";

    /// <summary>
    /// Root directory under which all template body files are stored.
    /// Supports local absolute paths and UNC paths (\\server\share\templates).
    /// Defaults to a "templates" subdirectory relative to the application root.
    /// </summary>
    public string RootPath { get; set; } = "templates";
}
