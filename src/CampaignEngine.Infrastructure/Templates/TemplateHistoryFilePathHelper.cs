namespace CampaignEngine.Infrastructure.Templates;

/// <summary>
/// Generates relative file-system paths for template body history copies.
///
/// US-005 TASK-005-02: History copy path helper.
///
/// Convention (F-105):
///   <c>templates/{templateId}/history/v{version}.docx</c>  (DOCX channel)
///   <c>templates/{templateId}/history/v{version}.html</c>  (Email/SMS channels)
///
/// The path is relative to the storage root configured in
/// <c>TemplateBodyStoreOptions.RootPath</c>. It never starts with a directory
/// separator so that <c>Path.Combine(root, relativePath)</c> works correctly
/// on all platforms.
///
/// Examples:
/// <list type="bullet">
///   <item><c>templates/3fa85f64-5717-4562-b3fc-2c963f66afa6/history/v1.docx</c></item>
///   <item><c>templates/3fa85f64-5717-4562-b3fc-2c963f66afa6/history/v2.html</c></item>
/// </list>
/// </summary>
public static class TemplateHistoryFilePathHelper
{
    private const string TemplatesFolder = "templates";
    private const string HistoryFolder = "history";

    /// <summary>
    /// Builds the relative history storage path for a template body file,
    /// inferring the extension from the source body path.
    /// </summary>
    /// <param name="templateId">The template's unique identifier (used as the sub-directory name).</param>
    /// <param name="version">The 1-based version number being archived to history.</param>
    /// <param name="sourceBodyPath">
    /// The existing body path whose extension (.docx, .html, etc.) is reused for the
    /// history copy. If <see langword="null"/> or empty, defaults to <c>.docx</c>.
    /// </param>
    /// <returns>
    /// A forward-slash separated relative path such as
    /// <c>templates/{templateId}/history/v{version}.docx</c>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="templateId"/> is <see cref="Guid.Empty"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="version"/> is less than 1.
    /// </exception>
    public static string Build(Guid templateId, int version, string? sourceBodyPath = null)
    {
        if (templateId == Guid.Empty)
            throw new ArgumentException("templateId must not be Guid.Empty.", nameof(templateId));

        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), version, "Version must be >= 1.");

        var extension = string.IsNullOrWhiteSpace(sourceBodyPath)
            ? ".docx"
            : Path.GetExtension(sourceBodyPath);

        if (string.IsNullOrWhiteSpace(extension))
            extension = ".docx";

        return string.Join("/",
            TemplatesFolder,
            templateId.ToString(),
            HistoryFolder,
            $"v{version}{extension}");
    }
}
