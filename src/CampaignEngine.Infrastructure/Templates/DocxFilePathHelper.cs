namespace CampaignEngine.Infrastructure.Templates;

/// <summary>
/// Generates relative file-system paths for DOCX template body files.
///
/// US-006 TASK-006-03: File naming convention helper.
///
/// Convention (F-106):
///   <c>templates/{templateId}/v{version}.docx</c>
///
/// The path is relative to the storage root configured in
/// <c>TemplateBodyStoreOptions.RootPath</c>. It never starts with a directory
/// separator so that <c>Path.Combine(root, relativePath)</c> works correctly
/// on all platforms.
///
/// Examples:
/// <list type="bullet">
///   <item><c>templates/3fa85f64-5717-4562-b3fc-2c963f66afa6/v1.docx</c></item>
///   <item><c>templates/3fa85f64-5717-4562-b3fc-2c963f66afa6/v2.docx</c></item>
/// </list>
/// </summary>
public static class DocxFilePathHelper
{
    private const string TemplatesFolder = "templates";
    private const string DocxExtension = ".docx";

    /// <summary>
    /// Builds the relative storage path for a DOCX template body file.
    /// </summary>
    /// <param name="templateId">The template's unique identifier (used as the sub-directory name).</param>
    /// <param name="version">The 1-based version number of the template.</param>
    /// <returns>
    /// A forward-slash separated relative path such as
    /// <c>templates/{templateId}/v{version}.docx</c>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="templateId"/> is <see cref="Guid.Empty"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="version"/> is less than 1.
    /// </exception>
    public static string Build(Guid templateId, int version)
    {
        if (templateId == Guid.Empty)
            throw new ArgumentException("templateId must not be Guid.Empty.", nameof(templateId));

        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), version, "Version must be >= 1.");

        // Use forward slashes for portability (Path.Combine normalises on the current OS,
        // but forward slashes work everywhere including Linux and UNC paths).
        return string.Join("/", TemplatesFolder, templateId.ToString(), $"v{version}{DocxExtension}");
    }
}
