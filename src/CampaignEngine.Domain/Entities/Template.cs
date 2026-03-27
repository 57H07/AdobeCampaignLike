using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Represents a reusable message template with dynamic content.
/// Templates support soft delete and version history.
/// </summary>
public class Template : SoftDeletableEntity
{
    public string Name { get; set; } = string.Empty;
    public ChannelType Channel { get; set; }
    public string HtmlBody { get; set; } = string.Empty;
    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;
    public int Version { get; set; } = 1;
    public bool IsSubTemplate { get; set; } = false;
    public string? Description { get; set; }

    // Navigation properties
    public ICollection<PlaceholderManifestEntry> PlaceholderManifests { get; set; } = new List<PlaceholderManifestEntry>();
    public ICollection<TemplateHistory> History { get; set; } = new List<TemplateHistory>();

    // ----------------------------------------------------------------
    // Domain behaviour
    // ----------------------------------------------------------------

    /// <summary>
    /// Transitions the template from Draft to Published.
    /// Call this after any external pre-conditions (e.g., manifest completeness) have been verified.
    /// </summary>
    /// <exception cref="DomainException">Thrown when the template is not in Draft status.</exception>
    public void Publish()
    {
        if (Status != TemplateStatus.Draft)
            throw new DomainException(
                $"Template '{Name}' cannot be published: current status is '{Status}'. " +
                "Only Draft templates can be published.");

        Status = TemplateStatus.Published;
    }

    /// <summary>
    /// Transitions the template to Archived status.
    /// Archived templates cannot be used in new campaigns or transitioned to any other status.
    /// </summary>
    /// <exception cref="DomainException">Thrown when the template is already Archived.</exception>
    public void Archive()
    {
        if (Status == TemplateStatus.Archived)
            throw new DomainException(
                $"Template '{Name}' is already Archived. Archived templates cannot change status.");

        Status = TemplateStatus.Archived;
    }
}
