using CampaignEngine.Application.DTOs.ApiKeys;
using CampaignEngine.Domain.Entities;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Service for managing API keys and authenticating inbound API requests.
/// Business rules:
///   1. Key values hashed with BCrypt before persistence — plaintext never stored.
///   2. Key prefix (first 8 chars) stored for display/identification.
///   3. Keys expire after 1 year by default (configurable per key).
///   4. Rate limiting: 1000 requests/minute per key by default (configurable).
///   5. Only Admin role can create, revoke, or rotate keys.
/// </summary>
public interface IApiKeyService
{
    // ----------------------------------------------------------------
    // Management operations (Admin only)
    // ----------------------------------------------------------------

    /// <summary>
    /// Generates a new API key, hashes it, and persists the key record.
    /// Returns the plaintext key in the response — this is the only time it is visible.
    /// </summary>
    Task<ApiKeyCreatedResponse> CreateAsync(
        CreateApiKeyRequest request,
        string createdByUserName,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all API keys (active and inactive).</summary>
    Task<IReadOnlyList<ApiKeyDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a single API key by ID, or null if not found.</summary>
    Task<ApiKeyDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an API key by setting IsActive = false.
    /// Revoked keys cannot be re-activated; a new key must be created.
    /// </summary>
    Task RevokeAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates an API key: revokes the existing key and creates a new one
    /// with the same name and settings.
    /// Returns the new key's created response (contains the new plaintext key).
    /// </summary>
    Task<ApiKeyCreatedResponse> RotateAsync(Guid id, string rotatedByUserName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the rate limit for an existing API key.
    /// Pass null for RateLimitPerMinute to reset to the system default.
    /// </summary>
    Task<ApiKeyDto> UpdateRateLimitAsync(Guid id, UpdateApiKeyRateLimitRequest request, CancellationToken cancellationToken = default);

    // ----------------------------------------------------------------
    // Authentication
    // ----------------------------------------------------------------

    /// <summary>
    /// Validates an inbound API key value.
    /// Returns the ApiKey entity if valid (active, not expired, hash matches); null otherwise.
    /// Also updates LastUsedAt on successful validation.
    /// </summary>
    Task<ApiKey?> ValidateAsync(string plaintextKey, CancellationToken cancellationToken = default);
}
