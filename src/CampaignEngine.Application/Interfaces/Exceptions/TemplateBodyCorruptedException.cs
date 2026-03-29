namespace CampaignEngine.Application.Interfaces.Exceptions;

/// <summary>
/// Exception thrown by <see cref="Storage.ITemplateBodyStore.ReadAsync"/> when the file
/// exists at the specified path but cannot be opened or parsed (e.g. truncated, locked,
/// or otherwise unreadable).
/// </summary>
/// <remarks>
/// This exception is part of the <see cref="Storage.ITemplateBodyStore"/> contract and is
/// defined in the Application layer so that Application services can catch and translate it
/// without taking a dependency on any Infrastructure implementation details.
/// <para>
/// It is distinct from <see cref="TemplateBodyNotFoundException"/>: that exception signals
/// the file is absent, whereas this one signals the file is present but unusable.
/// </para>
/// </remarks>
public class TemplateBodyCorruptedException : Exception
{
    /// <summary>
    /// The path of the file that exists but could not be read or parsed.
    /// </summary>
    public string? Path { get; }

    /// <summary>
    /// Initialises a new instance with the path of the corrupted file.
    /// </summary>
    /// <param name="path">The storage path of the file that could not be read.</param>
    public TemplateBodyCorruptedException(string? path)
        : base($"Template body file at path '{path}' exists but could not be opened or parsed.")
    {
        Path = path;
    }

    /// <summary>
    /// Initialises a new instance with the path of the corrupted file and an inner exception.
    /// </summary>
    /// <param name="path">The storage path of the file that could not be read.</param>
    /// <param name="innerException">The underlying I/O or parse exception.</param>
    public TemplateBodyCorruptedException(string? path, Exception innerException)
        : base(
            $"Template body file at path '{path}' exists but could not be opened or parsed.",
            innerException)
    {
        Path = path;
    }
}
