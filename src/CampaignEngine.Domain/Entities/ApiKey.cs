using CampaignEngine.Domain.Common;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Represents an API key for authenticating external consumers of the send API.
/// Key values are stored hashed (bcrypt); never in plaintext.
/// Business rules:
///   - Keys hashed with bcrypt before persistence.
///   - KeyPrefix (first 8 chars) stored in plaintext for display/identification.
///   - Keys expire after 1 year by default (configurable).
///   - Rate limiting per key (default 1000 requests/minute).
/// </summary>
public class ApiKey : AuditableEntity
{
    /// <summary>Human-readable label for this key (unique).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Bcrypt hash of the actual key value. The plaintext key is only returned at creation time.
    /// </summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    /// First 8 characters of the plaintext key, stored for display/identification purposes.
    /// Never exposes the full key — only used so admins can recognise which key is which.
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>Whether this key is active. Revoked keys have IsActive = false.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC datetime when the key expires. Null means never expires.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>UTC datetime of the most recent successful authentication with this key.</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Username of the admin who created this key.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Per-key rate limit in requests per minute.
    /// Null means use the system default (1000 req/min).
    /// </summary>
    public int? RateLimitPerMinute { get; set; }

    /// <summary>Optional description / notes about the intended consumer.</summary>
    public string? Description { get; set; }

    /// <summary>Returns true if the key has passed its expiration date.</summary>
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;

    /// <summary>
    /// Returns true if the key can be used for authentication (active and not expired).
    /// </summary>
    public bool IsValid => IsActive && !IsExpired;
}
