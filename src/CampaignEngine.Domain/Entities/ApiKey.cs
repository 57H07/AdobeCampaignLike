using CampaignEngine.Domain.Common;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Represents an API key for authenticating external consumers of the send API.
/// Key values are stored hashed (bcrypt); never in plaintext.
/// </summary>
public class ApiKey : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Bcrypt hash of the actual key value. The plaintext key is only returned at creation time.
    /// </summary>
    public string KeyHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Configurable rate limit override. Null means use the system default.
    /// </summary>
    public int? RateLimitPerMinute { get; set; }
}
