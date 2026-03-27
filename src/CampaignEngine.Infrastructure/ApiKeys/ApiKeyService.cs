using BCrypt.Net;
using CampaignEngine.Application.DTOs.ApiKeys;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Exceptions;
using Mapster;

namespace CampaignEngine.Infrastructure.ApiKeys;

/// <summary>
/// Infrastructure implementation of IApiKeyService.
/// Handles API key generation, BCrypt hashing, and validation.
///
/// Key format: "ce_" prefix + 32 random URL-safe Base64 characters.
/// Example: "ce_aBcDeFgH1234567890abcdefghijkl"
///
/// Security design:
///   - Plaintext key generated using cryptographically secure random bytes.
///   - Only BCrypt hash stored in the database; plaintext never persisted.
///   - Key prefix (first 8 chars) stored for display/identification only.
///   - BCrypt work factor: 10 (sufficient for authentication latency targets).
/// </summary>
public sealed class ApiKeyService : IApiKeyService
{
    // BCrypt work factor. 10 is the default; higher = slower but more secure.
    // At factor 10, BCrypt takes ~100ms per hash on modern hardware — acceptable
    // for admin operations but not a hot path.
    private const int BcryptWorkFactor = 10;

    // Key prefix to make CampaignEngine keys recognisable.
    private const string KeyPrefix = "ce_";

    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppLogger<ApiKeyService> _logger;

    public ApiKeyService(
        IApiKeyRepository apiKeyRepository,
        IUnitOfWork unitOfWork,
        IAppLogger<ApiKeyService> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ApiKeyCreatedResponse> CreateAsync(
        CreateApiKeyRequest request,
        string createdByUserName,
        CancellationToken cancellationToken = default)
    {
        // Check name uniqueness
        var exists = await _apiKeyRepository.ExistsWithNameAsync(request.Name, cancellationToken);

        if (exists)
            throw new ValidationException($"An API key with the name '{request.Name}' already exists.");

        // Generate the plaintext key
        var plaintextKey = GeneratePlaintextKey();

        // Hash with BCrypt
        var hash = BCrypt.Net.BCrypt.HashPassword(plaintextKey, BcryptWorkFactor);

        var entity = new ApiKey
        {
            Name = request.Name,
            Description = request.Description,
            KeyHash = hash,
            KeyPrefix = plaintextKey[..8],
            IsActive = true,
            ExpiresAt = request.ExpiresInDays.HasValue
                ? DateTime.UtcNow.AddDays(request.ExpiresInDays.Value)
                : null,
            RateLimitPerMinute = request.RateLimitPerMinute,
            CreatedBy = createdByUserName
        };

        await _apiKeyRepository.AddAsync(entity, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "API key created — Id={KeyId} Name={KeyName} CreatedBy={CreatedBy} ExpiresAt={ExpiresAt}",
            entity.Id, entity.Name, createdByUserName, entity.ExpiresAt);

        return new ApiKeyCreatedResponse
        {
            Key = entity.Adapt<ApiKeyDto>(),
            PlaintextKey = plaintextKey
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKeyDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var keys = await _apiKeyRepository.GetAllAsync(cancellationToken);
        return keys.Select(k => k.Adapt<ApiKeyDto>()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<ApiKeyDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _apiKeyRepository.GetByIdNoTrackingAsync(id, cancellationToken);
        return entity is null ? null : entity.Adapt<ApiKeyDto>();
    }

    /// <inheritdoc />
    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _apiKeyRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("ApiKey", id);

        if (!entity.IsActive)
            throw new ValidationException($"API key '{entity.Name}' is already revoked.");

        entity.IsActive = false;
        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogWarning(
            "API key revoked — Id={KeyId} Name={KeyName}", entity.Id, entity.Name);
    }

    /// <inheritdoc />
    public async Task<ApiKeyCreatedResponse> RotateAsync(
        Guid id,
        string rotatedByUserName,
        CancellationToken cancellationToken = default)
    {
        var existing = await _apiKeyRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("ApiKey", id);

        // Revoke the old key
        existing.IsActive = false;

        // Create a new key with the same configuration
        var newRequest = new CreateApiKeyRequest
        {
            Name = $"{existing.Name} (rotated {DateTime.UtcNow:yyyy-MM-dd})",
            Description = existing.Description,
            RateLimitPerMinute = existing.RateLimitPerMinute,
            ExpiresInDays = existing.ExpiresAt.HasValue
                ? (int)(existing.ExpiresAt.Value - DateTime.UtcNow).TotalDays
                : null
        };

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "API key rotated — OldId={OldKeyId} OldName={OldKeyName} RotatedBy={RotatedBy}",
            existing.Id, existing.Name, rotatedByUserName);

        return await CreateAsync(newRequest, rotatedByUserName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ApiKey?> ValidateAsync(string plaintextKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey))
            return null;

        // Extract prefix for fast DB filtering (avoid BCrypt-checking every key)
        if (plaintextKey.Length < 8)
            return null;

        var prefix = plaintextKey[..8];

        // Fetch candidate keys by prefix — should be 0 or 1 in practice
        var candidates = await _apiKeyRepository.GetActiveCandidatesByPrefixAsync(prefix, cancellationToken);

        foreach (var candidate in candidates)
        {
            // Check expiration first (cheap)
            if (candidate.IsExpired)
                continue;

            // BCrypt verification (slow by design — ~100ms)
            if (!BCrypt.Net.BCrypt.Verify(plaintextKey, candidate.KeyHash))
                continue;

            // Valid — update LastUsedAt
            candidate.LastUsedAt = DateTime.UtcNow;
            await _unitOfWork.CommitAsync(cancellationToken);

            return candidate;
        }

        return null;
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Generates a cryptographically secure API key.
    /// Format: "ce_" + 32 URL-safe Base64 chars = 35 chars total.
    /// </summary>
    private static string GeneratePlaintextKey()
    {
        var randomBytes = new byte[24]; // 24 bytes = 32 Base64 chars
        System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);
        var base64 = Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .Replace("=", "");
        return KeyPrefix + base64;
    }
}
