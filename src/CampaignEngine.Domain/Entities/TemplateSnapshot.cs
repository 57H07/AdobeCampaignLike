using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Immutable snapshot of template content frozen at campaign scheduling time.
/// Guarantees campaign reproducibility even if source template is modified.
/// </summary>
public class TemplateSnapshot : AuditableEntity
{
    public Guid OriginalTemplateId { get; set; }
    public int TemplateVersion { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChannelType Channel { get; set; }

    /// <summary>
    /// Fully resolved HTML body including all sub-template content.
    /// </summary>
    public string ResolvedHtmlBody { get; set; } = string.Empty;

    // Navigation property
    public ICollection<CampaignStep> CampaignSteps { get; set; } = new List<CampaignStep>();
}
