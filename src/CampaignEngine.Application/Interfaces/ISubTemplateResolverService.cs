using CampaignEngine.Domain.Common;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Service for resolving sub-template references within parent template bodies.
///
/// Business rules:
/// - Sub-template syntax: {{> subtemplate_name}}
/// - Sub-templates are resolved recursively, up to a maximum depth of 5 levels.
/// - Circular references (A -> B -> A) are detected and throw a ValidationException.
/// - Sub-templates inherit the channel from the parent template context.
/// </summary>
public interface ISubTemplateResolverService
{
    /// <summary>
    /// Resolves all sub-template references in the given HTML body, replacing
    /// each {{> name}} placeholder with the corresponding sub-template's HTML body.
    /// Resolution is recursive (up to MaxDepth levels).
    /// </summary>
    /// <param name="templateId">ID of the parent template being resolved (used for circular reference detection).</param>
    /// <param name="htmlBody">The HTML body containing {{> name}} sub-template references.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fully resolved HTML body with all sub-template references replaced.</returns>
    /// <exception cref="Domain.Exceptions.ValidationException">
    /// Thrown when a circular reference is detected or the maximum nesting depth is exceeded.
    /// </exception>
    Task<string> ResolveAsync(
        Guid templateId,
        string htmlBody,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts all direct sub-template references ({{> name}} syntax) from the given HTML body.
    /// Does not perform recursive resolution.
    /// </summary>
    /// <param name="htmlBody">The HTML body to inspect.</param>
    /// <returns>A collection of TemplateReference value objects for each distinct reference found.</returns>
    IReadOnlyList<TemplateReference> ExtractReferences(string htmlBody);

    /// <summary>
    /// Validates that no circular references exist starting from the specified template.
    /// Throws if a cycle is found.
    /// </summary>
    /// <param name="templateId">The template ID to start validation from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Domain.Exceptions.ValidationException">
    /// Thrown when a circular reference is detected.
    /// </exception>
    Task ValidateNoCircularReferencesAsync(
        Guid templateId,
        CancellationToken cancellationToken = default);
}
