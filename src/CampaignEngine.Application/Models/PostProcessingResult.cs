namespace CampaignEngine.Application.Models;

/// <summary>
/// Represents the output of a channel post-processor.
/// For Email: Content is the HTML string (UTF-8), ContentType is "text/html".
/// For Letter: Content is the PDF bytes, ContentType is "application/pdf".
/// For SMS: Content is the plain text string (UTF-8), ContentType is "text/plain".
/// </summary>
public sealed class PostProcessingResult
{
    /// <summary>
    /// The processed output as a UTF-8 string.
    /// For binary outputs (PDF), use <see cref="BinaryContent"/> instead.
    /// </summary>
    public string? TextContent { get; init; }

    /// <summary>
    /// The processed output as raw bytes (used for PDF / binary output).
    /// </summary>
    public byte[]? BinaryContent { get; init; }

    /// <summary>
    /// MIME content type of the output (e.g., "text/html", "application/pdf", "text/plain").
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Whether this result contains binary content.
    /// </summary>
    public bool IsBinary => BinaryContent is not null;

    /// <summary>
    /// Factory: create a text result (Email HTML or SMS plain text).
    /// </summary>
    public static PostProcessingResult Text(string content, string contentType = "text/html") =>
        new() { TextContent = content, ContentType = contentType };

    /// <summary>
    /// Factory: create a binary result (PDF for Letter channel).
    /// </summary>
    public static PostProcessingResult Binary(byte[] content, string contentType = "application/pdf") =>
        new() { BinaryContent = content, ContentType = contentType };
}
