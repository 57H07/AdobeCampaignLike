using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Application.DTOs.ApiKeys;

/// <summary>
/// Request body for updating the rate limit of an existing API key.
/// </summary>
public class UpdateApiKeyRateLimitRequest
{
    /// <summary>
    /// New rate limit in requests per minute.
    /// Null resets to the system default (1000 req/min).
    /// Must be between 1 and 100,000 if provided. Example: 500
    /// </summary>
    [Range(1, 100000)]
    public int? RateLimitPerMinute { get; init; }
}
