using CampaignEngine.Application.Models;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Strategy interface for channel-specific post-processing of rendered template content.
/// Each channel (Email, Letter, SMS) has different output format requirements:
/// - Email: CSS inlining + HTML sanitization (Outlook compatibility)
/// - Letter: HTML to PDF conversion (A4 format, embedded fonts)
/// - SMS: HTML stripping + truncation to 160 characters
///
/// Implementations are registered in DI; resolved by ChannelType at runtime.
/// </summary>
public interface IChannelPostProcessor
{
    /// <summary>
    /// The channel this post-processor handles.
    /// </summary>
    ChannelType Channel { get; }

    /// <summary>
    /// Processes rendered HTML content into the channel-appropriate output format.
    /// </summary>
    /// <param name="renderedHtml">The rendered HTML string from the template engine.</param>
    /// <param name="context">Optional post-processing context with metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Post-processed result containing the output bytes and content type.</returns>
    /// <exception cref="PostProcessingException">Thrown when conversion fails.</exception>
    Task<PostProcessingResult> ProcessAsync(
        string renderedHtml,
        PostProcessingContext? context = null,
        CancellationToken cancellationToken = default);
}
