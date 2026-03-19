using CampaignEngine.Application.Models;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Abstraction for template rendering engine (Scriban implementation in Infrastructure).
/// Stateless and thread-safe by contract.
/// All data values are HTML-escaped by default to prevent XSS.
/// Template HTML itself is trusted (Designer role only).
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>
    /// Resolves a template body with the provided data context.
    /// </summary>
    /// <param name="templateBody">Raw template content with placeholders.</param>
    /// <param name="data">Key-value data dictionary for substitution.</param>
    /// <param name="cancellationToken">Optional cancellation token. Timeout is 10 seconds by default.</param>
    /// <returns>Resolved content with all placeholders substituted.</returns>
    Task<string> RenderAsync(
        string templateBody,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a template body using a strongly-typed TemplateContext.
    /// </summary>
    /// <param name="templateBody">Raw template content with placeholders.</param>
    /// <param name="context">Structured rendering context with data and options.</param>
    /// <param name="cancellationToken">Optional cancellation token. Timeout is 10 seconds by default.</param>
    /// <returns>Resolved content with all placeholders substituted.</returns>
    Task<string> RenderAsync(
        string templateBody,
        TemplateContext context,
        CancellationToken cancellationToken = default);
}
