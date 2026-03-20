namespace CampaignEngine.Domain.Exceptions;

/// <summary>
/// Thrown when channel post-processing fails (CSS inlining, PDF conversion, HTML stripping).
/// May be a permanent failure (invalid input) or transient failure (PDF engine unavailable).
/// The <see cref="IsTransient"/> property indicates whether retry is appropriate.
/// </summary>
public class PostProcessingException : DomainException
{
    /// <summary>
    /// Indicates whether this failure may succeed on retry (transient infrastructure issue).
    /// When false, this is a permanent failure requiring manual intervention.
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>
    /// The channel that failed post-processing (for diagnostic logging).
    /// </summary>
    public string? Channel { get; }

    public PostProcessingException(string message, string? channel = null, bool isTransient = false)
        : base(message)
    {
        Channel = channel;
        IsTransient = isTransient;
    }

    public PostProcessingException(string message, Exception innerException, string? channel = null, bool isTransient = false)
        : base(message, innerException)
    {
        Channel = channel;
        IsTransient = isTransient;
    }
}
