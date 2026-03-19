namespace CampaignEngine.Domain.Exceptions;

/// <summary>
/// Thrown when template rendering fails due to a malformed template,
/// invalid syntax, timeout, or other rendering error.
/// This is a permanent failure (not retried — templates must be fixed by a Designer).
/// </summary>
public class TemplateRenderException : DomainException
{
    /// <summary>
    /// Position in the template where the error occurred, if available.
    /// </summary>
    public string? TemplateLocation { get; }

    public TemplateRenderException(string message)
        : base(message)
    {
    }

    public TemplateRenderException(string message, string? templateLocation)
        : base(message)
    {
        TemplateLocation = templateLocation;
    }

    public TemplateRenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public TemplateRenderException(string message, string? templateLocation, Exception innerException)
        : base(message, innerException)
    {
        TemplateLocation = templateLocation;
    }
}
