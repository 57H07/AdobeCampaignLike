namespace CampaignEngine.Application.DTOs.Campaigns;

/// <summary>
/// DTO representing a single step in a campaign sequence.
/// </summary>
public class CampaignStepDto
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Step position (1-based, ordered).</summary>
    public int StepOrder { get; init; }

    /// <summary>Channel type string: Email, Letter, or Sms.</summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>ID of the template to use for this step.</summary>
    public Guid TemplateId { get; init; }

    /// <summary>Name of the template (denormalized for display).</summary>
    public string? TemplateName { get; init; }

    /// <summary>Delay in days from the previous step (0 = immediate).</summary>
    public int DelayDays { get; init; }

    /// <summary>Optional JSON-serialized step-specific filter expression.</summary>
    public string? StepFilter { get; init; }

    /// <summary>UTC date/time when this step was scheduled.</summary>
    public DateTime? ScheduledAt { get; init; }

    /// <summary>UTC date/time when this step was executed.</summary>
    public DateTime? ExecutedAt { get; init; }
}
