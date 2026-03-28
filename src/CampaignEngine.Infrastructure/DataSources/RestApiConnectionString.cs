namespace CampaignEngine.Infrastructure.DataSources;

/// <summary>
/// Parses the REST API connector "connection string" — a semicolon-delimited key=value format
/// that encodes endpoint URL, authentication settings, and pagination configuration.
///
/// Connection string format (keys are case-insensitive):
///
///   Url=https://api.example.com/recipients;
///   Auth=ApiKey;
///   ApiKeyHeader=X-Api-Key;
///   ApiKeyValue=secret123;
///   DataPath=$.data;
///   PageParam=page;
///   PageSizeParam=per_page;
///   PageSize=100;
///   TotalPagesHeader=X-Total-Pages;
///   NextLinkHeader=Link;
///
/// Authentication modes:
///   None    — no authentication (default)
///   ApiKey  — sends an API key in a request header (ApiKeyHeader / ApiKeyValue)
///   Bearer  — sends a Bearer token in Authorization header (BearerToken)
///   OAuth2  — client_credentials flow (OAuth2TokenUrl / OAuth2ClientId / OAuth2ClientSecret / OAuth2Scope)
///
/// Pagination modes (auto-detected from keys present):
///   None       — single page (default)
///   PageParam  — ?page=N&amp;per_page=100 style
///   NextLink   — follows Link: &lt;url&gt;; rel="next" header
/// </summary>
public sealed class RestApiConnectionString
{
    // ----------------------------------------------------------------
    // Core endpoint
    // ----------------------------------------------------------------

    /// <summary>Base URL for the API endpoint (required).</summary>
    public string Url { get; private set; } = string.Empty;

    /// <summary>Optional static query parameters appended to every request (key=value pairs, comma-separated).</summary>
    public Dictionary<string, string> QueryParams { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    // ----------------------------------------------------------------
    // Authentication
    // ----------------------------------------------------------------

    /// <summary>Authentication mode. Default: None.</summary>
    public RestApiAuthMode AuthMode { get; private set; } = RestApiAuthMode.None;

    // API Key auth
    public string ApiKeyHeader { get; private set; } = "X-Api-Key";
    public string ApiKeyValue { get; private set; } = string.Empty;

    // Bearer token auth
    public string BearerToken { get; private set; } = string.Empty;

    // OAuth2 client credentials
    public string OAuth2TokenUrl { get; private set; } = string.Empty;
    public string OAuth2ClientId { get; private set; } = string.Empty;
    public string OAuth2ClientSecret { get; private set; } = string.Empty;
    public string OAuth2Scope { get; private set; } = string.Empty;

    // ----------------------------------------------------------------
    // JSON data path
    // ----------------------------------------------------------------

    /// <summary>
    /// JSONPath expression to the array of records within the response body.
    /// Supports simple dot-notation paths (e.g., "data", "results.items", "response.data.records").
    /// Use empty string / omit to treat the root as the array.
    /// Business rule: JSON path selector for nested data (US-017).
    /// </summary>
    public string DataPath { get; private set; } = string.Empty;

    // ----------------------------------------------------------------
    // Pagination
    // ----------------------------------------------------------------

    /// <summary>Pagination mode. Auto-detected from keys present.</summary>
    public RestApiPaginationMode PaginationMode { get; private set; } = RestApiPaginationMode.None;

    /// <summary>Query parameter name for page number (default: "page").</summary>
    public string PageParam { get; private set; } = "page";

    /// <summary>Query parameter name for page size (default: "per_page").</summary>
    public string PageSizeParam { get; private set; } = "per_page";

    /// <summary>Number of items per page (default: 100).</summary>
    public int PageSize { get; private set; } = 100;

    /// <summary>First page index (0 or 1 depending on the API). Default: 1.</summary>
    public int FirstPageIndex { get; private set; } = 1;

    /// <summary>Response header name containing total page count (e.g., "X-Total-Pages").</summary>
    public string TotalPagesHeader { get; private set; } = string.Empty;

    /// <summary>Response header name containing the next-page URL (RFC 5988 Link header, e.g., "Link").</summary>
    public string NextLinkHeader { get; private set; } = "Link";

    // ----------------------------------------------------------------
    // Factory / parser
    // ----------------------------------------------------------------

    public static RestApiConnectionString Parse(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));

        var result = new RestApiConnectionString();
        var keys = ParseKeyValuePairs(connectionString);

        // URL (required)
        if (!keys.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException(
                "REST API connection string must contain 'Url=<endpoint url>'.");

        result.Url = url.Trim();

        // Static query parameters (comma-separated list of key=value)
        if (keys.TryGetValue("queryparams", out var qp) && !string.IsNullOrWhiteSpace(qp))
        {
            foreach (var pair in qp.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0)
                    result.QueryParams[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
            }
        }

        // Authentication mode
        if (keys.TryGetValue("auth", out var authMode))
        {
            result.AuthMode = authMode.Trim().ToUpperInvariant() switch
            {
                "APIKEY" or "API_KEY" => RestApiAuthMode.ApiKey,
                "BEARER"              => RestApiAuthMode.Bearer,
                "OAUTH2" or "OAUTH"  => RestApiAuthMode.OAuth2,
                _                    => RestApiAuthMode.None
            };
        }

        // API key settings
        if (keys.TryGetValue("apikeyheader", out var apiKeyHeader))
            result.ApiKeyHeader = apiKeyHeader.Trim();
        if (keys.TryGetValue("apikeyvalue", out var apiKeyValue))
            result.ApiKeyValue = apiKeyValue.Trim();

        // Bearer token
        if (keys.TryGetValue("bearertoken", out var bearerToken))
            result.BearerToken = bearerToken.Trim();

        // OAuth2 settings
        if (keys.TryGetValue("oauth2tokenurl", out var tokenUrl))
            result.OAuth2TokenUrl = tokenUrl.Trim();
        if (keys.TryGetValue("oauth2clientid", out var clientId))
            result.OAuth2ClientId = clientId.Trim();
        if (keys.TryGetValue("oauth2clientsecret", out var clientSecret))
            result.OAuth2ClientSecret = clientSecret.Trim();
        if (keys.TryGetValue("oauth2scope", out var scope))
            result.OAuth2Scope = scope.Trim();

        // Data path (JSONPath into response)
        if (keys.TryGetValue("datapath", out var dataPath))
            result.DataPath = dataPath.Trim();

        // Pagination settings
        if (keys.TryGetValue("pageparam", out var pageParam))
        {
            result.PageParam = pageParam.Trim();
            result.PaginationMode = RestApiPaginationMode.PageParam;
        }

        if (keys.TryGetValue("pagesizeparam", out var pageSizeParam))
            result.PageSizeParam = pageSizeParam.Trim();

        if (keys.TryGetValue("pagesize", out var pageSizeStr) &&
            int.TryParse(pageSizeStr.Trim(), out var pageSize) && pageSize > 0)
            result.PageSize = pageSize;

        if (keys.TryGetValue("firstpageindex", out var firstPageStr) &&
            int.TryParse(firstPageStr.Trim(), out var firstPage))
            result.FirstPageIndex = firstPage;

        if (keys.TryGetValue("totalpagesheader", out var totalPagesHeader))
            result.TotalPagesHeader = totalPagesHeader.Trim();

        // Next-link pagination takes precedence over page-param if both are set
        if (keys.TryGetValue("nextlinkheader", out var nextLinkHeader) && !string.IsNullOrWhiteSpace(nextLinkHeader))
        {
            result.NextLinkHeader = nextLinkHeader.Trim();
            result.PaginationMode = RestApiPaginationMode.NextLink;
        }

        return result;
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Parses a semicolon-delimited "Key=Value;Key=Value" string into a case-insensitive dictionary.
    /// Values may contain '=' characters (only the first '=' in each segment is treated as the delimiter).
    /// Segments that are empty or have no '=' are silently skipped.
    /// </summary>
    private static Dictionary<string, string> ParseKeyValuePairs(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim();
            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;

            var key = trimmed[..eq].Trim().ToLowerInvariant();
            var value = trimmed[(eq + 1)..].Trim();
            result[key] = value;
        }

        return result;
    }
}

/// <summary>Authentication mode for REST API connector.</summary>
public enum RestApiAuthMode
{
    None   = 0,
    ApiKey = 1,
    Bearer = 2,
    OAuth2 = 3
}

/// <summary>Pagination mode for REST API connector.</summary>
public enum RestApiPaginationMode
{
    /// <summary>Single-page request — no pagination.</summary>
    None      = 0,

    /// <summary>Offset-based pagination using page number query parameters.</summary>
    PageParam = 1,

    /// <summary>Link-header based pagination following RFC 5988 rel="next".</summary>
    NextLink  = 2
}
