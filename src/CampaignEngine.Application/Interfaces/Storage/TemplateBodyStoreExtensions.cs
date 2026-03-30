namespace CampaignEngine.Application.Interfaces.Storage;

/// <summary>
/// Extension methods for <see cref="ITemplateBodyStore"/>.
///
/// US-007 TASK-007-03: Adds a convenience method to read an entire template body file
/// as a UTF-8 string. Implemented as an extension method (rather than an interface
/// member with a default implementation) so that Moq-based test mocks naturally
/// delegate to the mocked <see cref="ITemplateBodyStore.ReadAsync"/> without
/// requiring additional mock setup.
/// </summary>
public static class TemplateBodyStoreExtensions
{
    /// <summary>
    /// Reads the entire content of the template body file at the specified path as a
    /// UTF-8 encoded string. Intended for HTML body files (Email/SMS channels).
    /// </summary>
    /// <param name="store">The <see cref="ITemplateBodyStore"/> instance.</param>
    /// <param name="path">
    /// The logical storage path of the template body file.
    /// Must not be <see langword="null"/>, empty, or whitespace-only.
    /// </param>
    /// <param name="ct">A token that can be used to cancel the operation.</param>
    /// <returns>The full UTF-8 text content of the file.</returns>
    public static async Task<string> ReadAllTextAsync(
        this ITemplateBodyStore store,
        string path,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        await using var stream = await store.ReadAsync(path, ct);
        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }
}
