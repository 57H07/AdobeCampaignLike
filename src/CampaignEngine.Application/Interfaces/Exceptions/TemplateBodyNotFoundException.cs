namespace CampaignEngine.Application.Interfaces.Exceptions;

/// <summary>
/// Exception thrown by <see cref="Storage.ITemplateBodyStore.ReadAsync"/> when the requested
/// template body file does not exist at the specified path, or when the supplied path is
/// null or empty.
/// </summary>
/// <remarks>
/// This exception is part of the <see cref="Storage.ITemplateBodyStore"/> contract and is
/// defined in the Application layer so that Application services can catch and translate it
/// without taking a dependency on any Infrastructure implementation details.
/// </remarks>
public class TemplateBodyNotFoundException : Exception
{
    /// <summary>
    /// The path that was requested but could not be found.
    /// </summary>
    public string? Path { get; }

    /// <summary>
    /// Initialises a new instance with the path that was not found.
    /// </summary>
    /// <param name="path">The storage path that does not exist.</param>
    public TemplateBodyNotFoundException(string? path)
        : base($"Template body file not found at path: '{path}'.")
    {
        Path = path;
    }

    /// <summary>
    /// Initialises a new instance with the path and an inner exception.
    /// </summary>
    /// <param name="path">The storage path that does not exist.</param>
    /// <param name="innerException">The underlying I/O exception, if any.</param>
    public TemplateBodyNotFoundException(string? path, Exception innerException)
        : base($"Template body file not found at path: '{path}'.", innerException)
    {
        Path = path;
    }
}
