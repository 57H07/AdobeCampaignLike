namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// Lightweight summary DTO for listing templates in selectors and dropdowns.
/// Contains only identification and display fields; no HTML body.
/// </summary>
public class TemplateSummaryDto
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Display name (unique within channel).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Channel type string: Email, Letter, or Sms.</summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>Template lifecycle status: Draft, Published, Archived.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Whether this template is a reusable sub-template block.</summary>
    public bool IsSubTemplate { get; init; }

    /// <summary>Optional human-readable description.</summary>
    public string? Description { get; init; }
}
