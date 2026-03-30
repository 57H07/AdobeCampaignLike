namespace CampaignEngine.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the template storage root directory.
///
/// US-004 TASK-004-02: Strongly-typed options class for template storage path.
///
/// Bound from the "TemplateStorage" section in appsettings.json.
/// Validated at startup by <see cref="Startup.TemplateStorageStartupValidator"/>.
///
/// Example appsettings.json:
/// <code>
/// "TemplateStorage": {
///   "RootPath": "C:\\CampaignEngine\\templates"
/// }
/// </code>
/// </summary>
public class TemplateStorageOptions
{
    public const string SectionName = "TemplateStorage";

    /// <summary>
    /// Root directory under which all template body files are stored.
    /// Must be a non-empty path that exists and is writable before the application accepts requests.
    /// Supports local absolute paths and UNC paths (\\server\share\templates).
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
}
