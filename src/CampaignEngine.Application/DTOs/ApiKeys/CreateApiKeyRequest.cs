using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Application.DTOs.ApiKeys;

/// <summary>
/// Request body for creating a new API key.
/// The plaintext key value is returned only once in the response — copy it immediately.
/// </summary>
public class CreateApiKeyRequest
{
    /// <summary>Human-readable label for this key. Must be unique. Example: OrderService integration key</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional notes about the intended consumer of this key. Example: Used by the OrderService microservice.</summary>
    [MaxLength(500)]
    public string? Description { get; init; }

    /// <summary>
    /// Per-key rate limit in requests per minute.
    /// Null means use the system default (1000 req/min).
    /// Must be between 1 and 100000 if provided. Example: 500
    /// </summary>
    [Range(1, 100000)]
    public int? RateLimitPerMinute { get; init; }

    /// <summary>
    /// Key validity in days from now.
    /// Null means the key never expires.
    /// Default: 365 days (1 year) per business rule. Example: 365
    /// </summary>
    [Range(1, 3650)]
    public int? ExpiresInDays { get; init; } = 365;
}
