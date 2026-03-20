using System.Text.RegularExpressions;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Templates;

/// <summary>
/// Infrastructure implementation of ISubTemplateResolverService.
///
/// Resolves {{> subtemplate_name}} placeholders by loading the named sub-template
/// from the database and recursively resolving its body (up to MaxDepth levels).
///
/// Circular reference detection uses a visited-set (DFS) approach: if a template ID
/// is encountered twice in the same resolution chain, a ValidationException is thrown.
///
/// Business rules implemented:
///   BR-1: Sub-template syntax: {{> name}}
///   BR-2: Max recursion depth: 5
///   BR-3: Circular references throw ValidationException
///   BR-4: Sub-templates must have IsSubTemplate = true to be resolved by name
/// </summary>
public sealed class SubTemplateResolverService : ISubTemplateResolverService
{
    /// <summary>
    /// Maximum nesting depth for recursive sub-template resolution (BR-2).
    /// </summary>
    public const int MaxDepth = 5;

    // Matches {{> name}} or {{>name}} with optional whitespace
    private static readonly Regex SubTemplatePattern = new(
        @"\{\{>\s*(?<name>[a-zA-Z_][a-zA-Z0-9_\-\s]*?)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly CampaignEngineDbContext _dbContext;
    private readonly IAppLogger<SubTemplateResolverService> _logger;

    public SubTemplateResolverService(
        CampaignEngineDbContext dbContext,
        IAppLogger<SubTemplateResolverService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ResolveAsync(
        Guid templateId,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(htmlBody))
            return htmlBody;

        // Start recursive resolution with the root template ID in the visited chain
        var visitedChain = new List<Guid> { templateId };
        return await ResolveRecursiveAsync(htmlBody, visitedChain, depth: 0, cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyList<TemplateReference> ExtractReferences(string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(htmlBody))
            return Array.Empty<TemplateReference>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<TemplateReference>();

        foreach (Match match in SubTemplatePattern.Matches(htmlBody))
        {
            var name = match.Groups["name"].Value.Trim();
            if (seen.Add(name))
                references.Add(new TemplateReference(name));
        }

        return references.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task ValidateNoCircularReferencesAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var visited = new HashSet<Guid>();
        await DetectCircularReferencesAsync(templateId, visited, new Stack<Guid>(), cancellationToken);
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Recursively resolves sub-template references in the given HTML body.
    /// </summary>
    private async Task<string> ResolveRecursiveAsync(
        string htmlBody,
        List<Guid> visitedChain,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth >= MaxDepth)
        {
            _logger.LogWarning(
                "Sub-template resolution halted: maximum depth ({MaxDepth}) exceeded. Chain: [{Chain}]",
                MaxDepth,
                string.Join(" -> ", visitedChain));

            throw new ValidationException(
                $"Sub-template nesting depth exceeded the maximum of {MaxDepth} levels. " +
                $"Resolution chain: {string.Join(" -> ", visitedChain)}.");
        }

        var references = ExtractReferences(htmlBody);
        if (references.Count == 0)
            return htmlBody;

        // Resolve each unique sub-template name referenced in this body
        var resolved = htmlBody;

        foreach (var reference in references)
        {
            var subTemplate = await _dbContext.Templates
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => t.Name == reference.Name && t.IsSubTemplate,
                    cancellationToken);

            if (subTemplate is null)
            {
                _logger.LogWarning(
                    "Sub-template '{Name}' not found or not marked as IsSubTemplate=true. " +
                    "Placeholder left unresolved.",
                    reference.Name);
                // Leave the placeholder as-is (renderer will treat it as empty/unknown)
                continue;
            }

            // Circular reference detection
            if (visitedChain.Contains(subTemplate.Id))
            {
                var cycle = string.Join(" -> ", visitedChain) + $" -> {subTemplate.Id} ({subTemplate.Name})";
                var circularEx = new ValidationException(
                    $"Circular sub-template reference detected: {cycle}");
                _logger.LogError(circularEx, "Circular sub-template reference detected: {Cycle}", cycle);
                throw circularEx;
            }

            // Recurse: resolve the sub-template's body before embedding
            var subChain = new List<Guid>(visitedChain) { subTemplate.Id };
            var resolvedSubBody = await ResolveRecursiveAsync(
                subTemplate.HtmlBody,
                subChain,
                depth + 1,
                cancellationToken);

            // Replace ALL occurrences of {{> name}} with the resolved sub-template body
            var placeholderPattern = new Regex(
                @"\{\{>\s*" + Regex.Escape(reference.Name) + @"\s*\}\}",
                RegexOptions.IgnoreCase);

            resolved = placeholderPattern.Replace(resolved, resolvedSubBody);

            _logger.LogInformation(
                "Resolved sub-template '{Name}' (Id={SubTemplateId}) at depth {Depth}.",
                reference.Name, subTemplate.Id, depth + 1);
        }

        return resolved;
    }

    /// <summary>
    /// DFS traversal to detect circular references starting from templateId.
    /// </summary>
    private async Task DetectCircularReferencesAsync(
        Guid templateId,
        HashSet<Guid> globalVisited,
        Stack<Guid> currentPath,
        CancellationToken cancellationToken)
    {
        if (currentPath.Contains(templateId))
        {
            var cycle = string.Join(" -> ", currentPath.Reverse()) + $" -> {templateId}";
            throw new ValidationException(
                $"Circular sub-template reference detected: {cycle}");
        }

        if (globalVisited.Contains(templateId))
            return; // Already validated this branch

        globalVisited.Add(templateId);
        currentPath.Push(templateId);

        var template = await _dbContext.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        if (template is not null)
        {
            var references = ExtractReferences(template.HtmlBody);
            foreach (var reference in references)
            {
                var subTemplate = await _dbContext.Templates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        t => t.Name == reference.Name && t.IsSubTemplate,
                        cancellationToken);

                if (subTemplate is not null)
                {
                    await DetectCircularReferencesAsync(
                        subTemplate.Id, globalVisited, currentPath, cancellationToken);
                }
            }
        }

        currentPath.Pop();
    }
}
