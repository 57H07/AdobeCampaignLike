using CampaignEngine.Application.Interfaces.Exceptions;

namespace CampaignEngine.Application.Interfaces.Storage;

/// <summary>
/// Abstracts binary read/write/delete operations for template body files
/// (HTML for Email/SMS, DOCX for Letter).
/// </summary>
/// <remarks>
/// Implementations live in the Infrastructure layer and may target a local/network
/// file system, a cloud blob store, or any other backing store. Application services
/// depend only on this interface and never on concrete storage types.
/// <para>
/// The two exception types that form part of this contract —
/// <see cref="TemplateBodyNotFoundException"/> and <see cref="TemplateBodyCorruptedException"/> —
/// are defined in the Application layer so that both the interface and its callers share them
/// without any Infrastructure dependency.
/// </para>
/// </remarks>
public interface ITemplateBodyStore
{
    /// <summary>
    /// Opens and returns the content of the template body file at the specified path
    /// as a readable, seekable <see cref="Stream"/>.
    /// </summary>
    /// <param name="path">
    /// The logical storage path of the template body file.
    /// Must not be <see langword="null"/>, empty, or whitespace-only.
    /// </param>
    /// <param name="ct">A token that can be used to cancel the operation.</param>
    /// <returns>
    /// A <see cref="Stream"/> positioned at the beginning of the file content.
    /// The caller is responsible for disposing the returned stream.
    /// </returns>
    /// <exception cref="TemplateBodyNotFoundException">
    /// Thrown when <paramref name="path"/> is <see langword="null"/>, empty, whitespace-only,
    /// or points to a file that does not exist in the backing store.
    /// </exception>
    /// <exception cref="TemplateBodyCorruptedException">
    /// Thrown when the file exists at <paramref name="path"/> but cannot be opened
    /// or its content cannot be parsed (e.g. the file is locked, truncated, or otherwise
    /// unreadable).
    /// </exception>
    Task<Stream> ReadAsync(string path, CancellationToken ct = default);


    /// <summary>
    /// Persists the content from <paramref name="content"/> to the specified path in the
    /// backing store, creating or overwriting any existing file at that location.
    /// </summary>
    /// <param name="path">
    /// The logical storage path at which the template body should be written.
    /// Must not be <see langword="null"/>, empty, or whitespace-only.
    /// </param>
    /// <param name="content">
    /// A readable <see cref="Stream"/> whose bytes will be written to the store.
    /// The stream must be positioned at the desired start offset before calling this method.
    /// </param>
    /// <param name="ct">A token that can be used to cancel the operation.</param>
    /// <returns>
    /// The resolved storage path at which the content was persisted.
    /// Implementations may normalise or adjust the supplied path (e.g. add a file extension)
    /// and return the canonical form for subsequent <see cref="ReadAsync"/> calls.
    /// </returns>
    Task<string> WriteAsync(string path, Stream content, CancellationToken ct = default);

    /// <summary>
    /// Removes the template body file at the specified path from the backing store.
    /// </summary>
    /// <param name="path">
    /// The logical storage path of the template body file to delete.
    /// Must not be <see langword="null"/>, empty, or whitespace-only.
    /// </param>
    /// <param name="ct">A token that can be used to cancel the operation.</param>
    /// <remarks>
    /// Implementations should treat a missing file as a no-op rather than throwing,
    /// because <see cref="DeleteAsync"/> is typically called during cleanup and the
    /// file may already have been removed.
    /// </remarks>
    Task DeleteAsync(string path, CancellationToken ct = default);
}
