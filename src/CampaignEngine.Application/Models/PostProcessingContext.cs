namespace CampaignEngine.Application.Models;

/// <summary>
/// Optional metadata passed to a channel post-processor.
/// Processors may use this for contextual decisions (e.g. recipient data, campaign ID).
/// </summary>
public sealed class PostProcessingContext
{
    /// <summary>
    /// Optional campaign ID for correlation in logs.
    /// </summary>
    public Guid? CampaignId { get; init; }

    /// <summary>
    /// Optional recipient identifier for correlation in logs.
    /// </summary>
    public string? RecipientId { get; init; }

    /// <summary>
    /// Optional base URL for resolving relative CSS/image URLs in HTML.
    /// Used by the PDF converter to correctly load embedded resources.
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Optional maximum character count override (SMS channel).
    /// When null, the default limit of 160 characters applies.
    /// </summary>
    public int? SmsMaxLength { get; init; }
}
