namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Abstraction for template rendering engine (Scriban implementation in Infrastructure).
/// Stateless and thread-safe by contract.
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>
    /// Resolves a template body with the provided data context.
    /// </summary>
    /// <param name="templateBody">Raw template content with placeholders.</param>
    /// <param name="data">Key-value data dictionary for substitution.</param>
    /// <returns>Resolved content with all placeholders substituted.</returns>
    Task<string> RenderAsync(string templateBody, IDictionary<string, object?> data);
}
