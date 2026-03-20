namespace CampaignEngine.Application.DTOs.ApiKeys;

/// <summary>
/// DTO representing an API key returned from management endpoints.
/// The plaintext key value is NEVER included here (only on creation via ApiKeyCreatedResponse).
/// </summary>
public class ApiKeyDto
{
    public Guid Id { get; init; }

    /// <summary>Human-readable label for this key.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// First 8 characters of the plaintext key, for identification purposes.
    /// Shows as "ce_xxxxx..." in the UI.
    /// </summary>
    public string KeyPrefix { get; init; } = string.Empty;

    /// <summary>Whether this key is currently active.</summary>
    public bool IsActive { get; init; }

    /// <summary>Whether this key has passed its expiration date.</summary>
    public bool IsExpired { get; init; }

    /// <summary>UTC expiration time. Null means the key never expires.</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>UTC datetime of the last successful API call with this key.</summary>
    public DateTime? LastUsedAt { get; init; }

    /// <summary>Username of the admin who created this key.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Per-key rate limit override (requests per minute). Null = system default.</summary>
    public int? RateLimitPerMinute { get; init; }

    /// <summary>Optional notes about the intended consumer.</summary>
    public string? Description { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
