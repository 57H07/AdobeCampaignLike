namespace CampaignEngine.Application.DTOs.ApiKeys;

/// <summary>
/// Response returned when a new API key is created.
/// The plaintext key value is included ONLY in this response — it cannot be retrieved again.
/// The caller must copy it immediately.
/// </summary>
public class ApiKeyCreatedResponse
{
    /// <summary>The created key's metadata (same as ApiKeyDto).</summary>
    public ApiKeyDto Key { get; init; } = null!;

    /// <summary>
    /// The full plaintext API key value. This is shown ONCE and cannot be retrieved again.
    /// Store it securely — it is never persisted in plaintext.
    /// </summary>
    public string PlaintextKey { get; init; } = string.Empty;
}
