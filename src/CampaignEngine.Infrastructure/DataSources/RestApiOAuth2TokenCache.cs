using System.Net.Http.Json;
using System.Text.Json;

namespace CampaignEngine.Infrastructure.DataSources;

/// <summary>
/// Manages OAuth2 client-credentials token acquisition and caching for the REST API connector.
///
/// Tokens are cached in-memory per (tokenUrl + clientId) key and automatically refreshed
/// when they expire (with a 30-second safety margin before actual expiry).
///
/// Thread-safety: uses SemaphoreSlim to prevent thundering-herd on token refresh.
/// Registered as Singleton so the cache survives across scoped connector instances.
/// </summary>
public sealed class RestApiOAuth2TokenCache
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Key: "{tokenUrl}|{clientId}" -> (token, expiry)
    private readonly Dictionary<string, (string Token, DateTimeOffset Expiry)> _cache = new();

    /// <summary>Safety margin before expiry to trigger proactive refresh.</summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(30);

    public RestApiOAuth2TokenCache(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Returns a valid access token for the given OAuth2 client credentials configuration.
    /// Fetches or refreshes the token as needed.
    /// </summary>
    public async Task<string> GetTokenAsync(
        string tokenUrl,
        string clientId,
        string clientSecret,
        string scope,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{tokenUrl}|{clientId}";

        // Fast path: check cache without lock
        if (_cache.TryGetValue(cacheKey, out var cached) &&
            cached.Expiry > DateTimeOffset.UtcNow + ExpiryMargin)
        {
            return cached.Token;
        }

        // Slow path: acquire lock and refresh
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(cacheKey, out cached) &&
                cached.Expiry > DateTimeOffset.UtcNow + ExpiryMargin)
            {
                return cached.Token;
            }

            var (token, expiresIn) = await FetchTokenAsync(
                tokenUrl, clientId, clientSecret, scope, cancellationToken);

            var expiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            _cache[cacheKey] = (token, expiry);

            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ----------------------------------------------------------------
    // Token request
    // ----------------------------------------------------------------

    private async Task<(string Token, int ExpiresIn)> FetchTokenAsync(
        string tokenUrl,
        string clientId,
        string clientSecret,
        string scope,
        CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient("RestApiConnector");
        client.Timeout = TimeSpan.FromSeconds(30);

        var formContent = new Dictionary<string, string>
        {
            ["grant_type"]    = "client_credentials",
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret
        };

        if (!string.IsNullOrWhiteSpace(scope))
            formContent["scope"] = scope;

        using var requestContent = new FormUrlEncodedContent(formContent);
        using var response = await client.PostAsync(tokenUrl, requestContent, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"OAuth2 token request failed with HTTP {(int)response.StatusCode}: {errorBody}");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken: cancellationToken);

        if (!json.TryGetProperty("access_token", out var accessTokenElem))
            throw new InvalidOperationException(
                "OAuth2 token response does not contain 'access_token'.");

        var token = accessTokenElem.GetString()
            ?? throw new InvalidOperationException("OAuth2 'access_token' is null.");

        var expiresIn = 3600; // default 1 hour if not present
        if (json.TryGetProperty("expires_in", out var expiresElem) &&
            expiresElem.ValueKind == JsonValueKind.Number)
        {
            expiresIn = expiresElem.GetInt32();
        }

        return (token, expiresIn);
    }
}
