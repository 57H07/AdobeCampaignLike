using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.DTOs.Campaigns;

/// <summary>
/// DTO representing a campaign returned from the API.
/// </summary>
public class CampaignDto
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Campaign display name (unique).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Campaign lifecycle status.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>ID of the linked data source for recipient targeting.</summary>
    public Guid? DataSourceId { get; init; }

    /// <summary>Name of the linked data source (denormalized for display).</summary>
    public string? DataSourceName { get; init; }

    /// <summary>JSON-serialized filter expression AST for recipient targeting.</summary>
    public string? FilterExpression { get; init; }

    /// <summary>JSON-serialized dictionary of operator-provided free field values.</summary>
    public string? FreeFieldValues { get; init; }

    /// <summary>UTC date/time when the campaign is scheduled to run. Null = not yet scheduled.</summary>
    public DateTime? ScheduledAt { get; init; }

    /// <summary>UTC date/time when the campaign started executing.</summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>UTC date/time when the campaign completed.</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>Total number of recipients targeted.</summary>
    public int TotalRecipients { get; init; }

    /// <summary>Number of recipients that have been processed.</summary>
    public int ProcessedCount { get; init; }

    /// <summary>Number of successful sends.</summary>
    public int SuccessCount { get; init; }

    /// <summary>Number of failed sends.</summary>
    public int FailureCount { get; init; }

    /// <summary>Username of the operator who created the campaign.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Campaign steps in order.</summary>
    public IReadOnlyList<CampaignStepDto> Steps { get; init; } = Array.Empty<CampaignStepDto>();

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>UTC last-update timestamp.</summary>
    public DateTime UpdatedAt { get; init; }
}
