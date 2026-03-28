using System.Net.Http.Headers;
using System.Text.Json;
using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Configuration;

namespace CampaignEngine.Infrastructure.DataSources;

/// <summary>
/// REST API implementation of IDataSourceConnector.
///
/// Design principles:
/// - Supports GET endpoints with static query parameters.
/// - JSON response body is parsed via a configurable dot-notation data path.
/// - Authentication: None, API Key (header), Bearer token, OAuth2 client credentials.
/// - Pagination: None, page-param (?page=N&amp;per_page=100), or Link-header (RFC 5988 rel="next").
/// - Retry: up to 3 attempts with exponential backoff on transient HTTP failures (5xx, 429, timeout).
/// - Response size guard: 50 MB limit.
/// - Timeout: 60 seconds per request (configurable via RestApiConnectorOptions).
///
/// Connection string format: semicolon-delimited Key=Value pairs.
/// See <see cref="RestApiConnectionString"/> for full schema.
///
/// Business rules (US-017):
/// 1. API timeout: 60 seconds
/// 2. Retry policy: 3 attempts with exponential backoff
/// 3. Response size limit: 50 MB
/// 4. JSON path selector for nested data
/// </summary>
public sealed class RestApiConnector : IDataSourceConnector
{
    private readonly RestApiConnectorOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RestApiOAuth2TokenCache _oauth2Cache;
    private readonly IAppLogger<RestApiConnector> _logger;

    public RestApiConnector(
        RestApiConnectorOptions options,
        IHttpClientFactory httpClientFactory,
        RestApiOAuth2TokenCache oauth2Cache,
        IAppLogger<RestApiConnector> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(oauth2Cache);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _httpClientFactory = httpClientFactory;
        _oauth2Cache = oauth2Cache;
        _logger = logger;
    }

    // ----------------------------------------------------------------
    // IDataSourceConnector.QueryAsync
    // ----------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>
    /// Fetches all pages from the REST endpoint.
    /// REST APIs do not support the FilterExpressionDto AST natively,
    /// so filters are applied in-memory after fetching all data.
    /// </remarks>
    public async Task<IReadOnlyList<IDictionary<string, object?>>> QueryAsync(
        DataSourceDefinitionDto definition,
        IReadOnlyList<FilterExpressionDto>? filters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var config = RestApiConnectionString.Parse(definition.ConnectionString);

        _logger.LogInformation(
            "RestApiConnector: querying DataSource {DataSourceId}, Url={Url}, Pagination={Pagination}",
            definition.Id, config.Url, config.PaginationMode);

        var allRows = await FetchAllPagesAsync(config, cancellationToken);

        _logger.LogInformation(
            "RestApiConnector: DataSource {DataSourceId} returned {RowCount} total rows",
            definition.Id, allRows.Count);

        // Apply in-memory filters if provided
        if (filters is { Count: > 0 })
        {
            allRows = ApplyFilters(allRows, filters);
            _logger.LogDebug(
                "RestApiConnector: after filter application, {RowCount} rows remain",
                allRows.Count);
        }

        return allRows;
    }

    // ----------------------------------------------------------------
    // IDataSourceConnector.GetSchemaAsync
    // ----------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>
    /// Fetches the first page (single request) and infers schema from the first JSON record.
    /// Field types are inferred from JSON value kinds.
    /// All inferred fields are marked as filterable.
    /// </remarks>
    public async Task<IReadOnlyList<FieldDefinitionDto>> GetSchemaAsync(
        DataSourceDefinitionDto definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var config = RestApiConnectionString.Parse(definition.ConnectionString);

        _logger.LogInformation(
            "RestApiConnector: discovering schema for DataSource {DataSourceId}, Url={Url}",
            definition.Id, config.Url);

        // Fetch first page only for schema discovery
        var (rows, _) = await FetchPageAsync(config, extraParams: null, overrideUrl: null, cancellationToken);

        if (rows.Count == 0)
        {
            _logger.LogWarning(
                "RestApiConnector: schema discovery for DataSource {DataSourceId} returned no rows",
                definition.Id);
            return [];
        }

        var fields = rows[0]
            .Select(kv => new FieldDefinitionDto
            {
                FieldName    = kv.Key,
                DisplayName  = kv.Key,
                FieldType    = InferFieldType(kv.Value),
                IsFilterable = true
            })
            .ToList();

        _logger.LogInformation(
            "RestApiConnector: schema discovery for DataSource {DataSourceId} inferred {FieldCount} fields",
            definition.Id, fields.Count);

        return fields;
    }

    // ----------------------------------------------------------------
    // Pagination: fetch all pages
    // ----------------------------------------------------------------

    private async Task<List<IDictionary<string, object?>>> FetchAllPagesAsync(
        RestApiConnectionString config,
        CancellationToken cancellationToken)
    {
        return config.PaginationMode switch
        {
            RestApiPaginationMode.PageParam => await FetchByPageParamAsync(config, cancellationToken),
            RestApiPaginationMode.NextLink  => await FetchByNextLinkAsync(config, cancellationToken),
            _                              => (await FetchPageAsync(config, null, null, cancellationToken)).Rows
        };
    }

    /// <summary>
    /// Offset-based pagination: sends ?page=N&amp;per_page=P.
    /// Stops when the page returns fewer rows than page size, TotalPagesHeader is exhausted, or MaxPages is reached.
    /// </summary>
    private async Task<List<IDictionary<string, object?>>> FetchByPageParamAsync(
        RestApiConnectionString config,
        CancellationToken cancellationToken)
    {
        var allRows = new List<IDictionary<string, object?>>();
        int page = config.FirstPageIndex;
        int? totalPages = null;

        for (int i = 0; i < _options.MaxPages; i++, page++)
        {
            if (totalPages.HasValue && page > totalPages.Value)
                break;

            var extraParams = new Dictionary<string, string>
            {
                [config.PageParam]     = page.ToString(),
                [config.PageSizeParam] = config.PageSize.ToString()
            };

            var (pageRows, headers) = await FetchPageAsync(config, extraParams, overrideUrl: null, cancellationToken);
            allRows.AddRange(pageRows);

            // Try to read TotalPages from response header (first page only)
            if (!totalPages.HasValue && !string.IsNullOrWhiteSpace(config.TotalPagesHeader))
            {
                if (headers.TryGetValues(config.TotalPagesHeader, out var vals) &&
                    int.TryParse(vals.FirstOrDefault(), out var tp))
                {
                    totalPages = tp;
                    _logger.LogDebug(
                        "RestApiConnector: TotalPages={TotalPages} from header '{Header}'",
                        tp, config.TotalPagesHeader);
                }
            }

            // Last page: fewer items than page size
            if (pageRows.Count < config.PageSize)
                break;
        }

        return allRows;
    }

    /// <summary>
    /// Link-header pagination: follows the rel="next" URL from the Link response header.
    /// Stops when there is no rel="next" or MaxPages is reached.
    /// </summary>
    private async Task<List<IDictionary<string, object?>>> FetchByNextLinkAsync(
        RestApiConnectionString config,
        CancellationToken cancellationToken)
    {
        var allRows = new List<IDictionary<string, object?>>();
        string? nextUrl = null;   // null = use config.Url for first request

        for (int page = 0; page < _options.MaxPages; page++)
        {
            var (pageRows, headers) = await FetchPageAsync(config, null, nextUrl, cancellationToken);
            allRows.AddRange(pageRows);

            nextUrl = ExtractNextLink(headers, config.NextLinkHeader);
            if (string.IsNullOrWhiteSpace(nextUrl))
                break;

            _logger.LogDebug(
                "RestApiConnector: Link pagination next URL: {NextUrl}", nextUrl);
        }

        return allRows;
    }

    // ----------------------------------------------------------------
    // HTTP: fetch a single page → (rows, response headers)
    // ----------------------------------------------------------------

    private async Task<(List<IDictionary<string, object?>> Rows, HttpResponseHeaders Headers)> FetchPageAsync(
        RestApiConnectionString config,
        Dictionary<string, string>? extraParams,
        string? overrideUrl,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(config, extraParams, overrideUrl, cancellationToken);
        var body = await ReadResponseBodyAsync(response, cancellationToken);
        var rows = ParseJsonToRows(body, config.DataPath);
        return (rows, response.Headers);
    }

    // ----------------------------------------------------------------
    // HTTP: send with retry (exponential backoff)
    // ----------------------------------------------------------------

    /// <summary>
    /// Sends the HTTP GET request with exponential backoff retry on transient failures.
    /// Transient: 429, 5xx, HttpRequestException, timeout (TaskCanceledException without cancellation).
    /// Business rule: 3 attempts (US-017).
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        RestApiConnectionString config,
        Dictionary<string, string>? extraParams,
        string? overrideUrl,
        CancellationToken cancellationToken)
    {
        var maxAttempts = _options.MaxRetryAttempts;
        var attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                var client = await BuildHttpClientAsync(config, cancellationToken);
                var url = BuildUrl(overrideUrl ?? config.Url, config.QueryParams, extraParams);

                _logger.LogDebug(
                    "RestApiConnector: GET {Url} (attempt {Attempt}/{Max})",
                    url, attempt, maxAttempts);

                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (IsTransientHttpError(response.StatusCode) && attempt < maxAttempts)
                {
                    response.Dispose();
                    await DelayRetryAsync(attempt, cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(
                    "RestApiConnector: HTTP request failed (attempt {Attempt}/{Max}): {Error}",
                    attempt, maxAttempts, ex.Message);
                await DelayRetryAsync(attempt, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested
                                                    && attempt < maxAttempts)
            {
                _logger.LogWarning(
                    "RestApiConnector: request timed out (attempt {Attempt}/{Max}): {Error}",
                    attempt, maxAttempts, ex.Message);
                await DelayRetryAsync(attempt, cancellationToken);
            }
        }
    }

    // ----------------------------------------------------------------
    // HTTP: authentication — TASK-017-03
    // ----------------------------------------------------------------

    /// <summary>
    /// Creates and configures an HttpClient with the appropriate authentication headers.
    /// Supported modes: None, ApiKey, Bearer, OAuth2 client credentials.
    /// Business rule: 60-second timeout (US-017).
    /// </summary>
    private async Task<HttpClient> BuildHttpClientAsync(
        RestApiConnectionString config,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("RestApiConnector");
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        switch (config.AuthMode)
        {
            case RestApiAuthMode.ApiKey:
                if (!string.IsNullOrWhiteSpace(config.ApiKeyValue))
                    client.DefaultRequestHeaders.Add(config.ApiKeyHeader, config.ApiKeyValue);
                break;

            case RestApiAuthMode.Bearer:
                if (!string.IsNullOrWhiteSpace(config.BearerToken))
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", config.BearerToken);
                break;

            case RestApiAuthMode.OAuth2:
                var token = await _oauth2Cache.GetTokenAsync(
                    config.OAuth2TokenUrl,
                    config.OAuth2ClientId,
                    config.OAuth2ClientSecret,
                    config.OAuth2Scope,
                    cancellationToken);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
                break;

            case RestApiAuthMode.None:
            default:
                break;
        }

        return client;
    }

    // ----------------------------------------------------------------
    // Response size guard (50 MB business rule)
    // ----------------------------------------------------------------

    private async Task<string> ReadResponseBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limitedStream = new LimitedStream(stream, _options.MaxResponseSizeBytes);
        using var reader = new StreamReader(limitedStream);

        try
        {
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (LimitedStream.LimitExceededException)
        {
            throw new InvalidOperationException(
                $"REST API response exceeds the maximum allowed size of " +
                $"{_options.MaxResponseSizeBytes / (1024 * 1024)} MB.");
        }
    }

    // ----------------------------------------------------------------
    // JSON parsing — TASK-017-02: JSON to data row mapping
    // ----------------------------------------------------------------

    /// <summary>
    /// Parses a JSON response body into a list of row dictionaries.
    ///
    /// The <paramref name="dataPath"/> is a dot-notation path to the array property
    /// (e.g., "data", "results.items"). Empty/null treats the root as the array.
    ///
    /// Supported shapes:
    ///   - JSON array at root or at dataPath → list of rows
    ///   - JSON object (single record) → wrapped in single-element list
    ///
    /// Value mapping:
    ///   - String  → string
    ///   - Number  → long (integer) or double (fractional)
    ///   - Bool    → bool
    ///   - Null    → null
    ///   - Object/Array → serialized JSON string (for flat-row compatibility)
    /// </summary>
    internal static List<IDictionary<string, object?>> ParseJsonToRows(
        string jsonBody,
        string dataPath)
    {
        if (string.IsNullOrWhiteSpace(jsonBody))
            return [];

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(jsonBody).RootElement;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"REST API response is not valid JSON: {ex.Message}", ex);
        }

        var dataElement = string.IsNullOrWhiteSpace(dataPath)
            ? root
            : NavigatePath(root, dataPath);

        return dataElement.ValueKind switch
        {
            JsonValueKind.Array  => ParseJsonArray(dataElement),
            JsonValueKind.Object => [ParseJsonObject(dataElement)],
            _                    => []
        };
    }

    /// <summary>
    /// Navigates a dot-notation path (e.g., "data.items") within a JsonElement.
    /// Returns the current element on path miss (graceful degradation).
    /// </summary>
    private static JsonElement NavigatePath(JsonElement element, string path)
    {
        var current = element;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out var next))
                return current;
            current = next;
        }
        return current;
    }

    private static List<IDictionary<string, object?>> ParseJsonArray(JsonElement array)
    {
        var rows = new List<IDictionary<string, object?>>(array.GetArrayLength());
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object)
                rows.Add(ParseJsonObject(element));
        }
        return rows;
    }

    private static IDictionary<string, object?> ParseJsonObject(JsonElement obj)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in obj.EnumerateObject())
            row[property.Name] = ConvertJsonValue(property.Value);
        return row;
    }

    private static object? ConvertJsonValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String    => element.GetString(),
        JsonValueKind.Number    => element.TryGetInt64(out var l) ? (object?)l : element.GetDouble(),
        JsonValueKind.True      => true,
        JsonValueKind.False     => false,
        JsonValueKind.Null      => null,
        JsonValueKind.Undefined => null,
        _                       => element.GetRawText()  // objects/arrays as JSON string
    };

    // ----------------------------------------------------------------
    // In-memory filter application
    // ----------------------------------------------------------------

    private static List<IDictionary<string, object?>> ApplyFilters(
        List<IDictionary<string, object?>> rows,
        IReadOnlyList<FilterExpressionDto> filters)
    {
        return rows.Where(row => filters.All(f => EvaluateFilter(row, f))).ToList();
    }

    private static bool EvaluateFilter(IDictionary<string, object?> row, FilterExpressionDto filter)
    {
        // Composite node
        if (filter.Children is { Count: > 0 })
        {
            var isOr = string.Equals(filter.LogicalOperator, "OR", StringComparison.OrdinalIgnoreCase);
            return isOr
                ? filter.Children.Any(c => EvaluateFilter(row, c))
                : filter.Children.All(c => EvaluateFilter(row, c));
        }

        // Leaf node
        if (string.IsNullOrWhiteSpace(filter.FieldName) || string.IsNullOrWhiteSpace(filter.Operator))
            return true;

        row.TryGetValue(filter.FieldName, out var rowValue);

        return filter.Operator.Trim().ToUpperInvariant() switch
        {
            "=" or "==" => CompareValues(rowValue, filter.Value) == 0,
            "!=" or "<>" => CompareValues(rowValue, filter.Value) != 0,
            ">"          => CompareValues(rowValue, filter.Value) > 0,
            "<"          => CompareValues(rowValue, filter.Value) < 0,
            ">="         => CompareValues(rowValue, filter.Value) >= 0,
            "<="         => CompareValues(rowValue, filter.Value) <= 0,
            "LIKE"       => MatchLike(rowValue?.ToString(), filter.Value?.ToString(), negated: false),
            "NOT LIKE"   => MatchLike(rowValue?.ToString(), filter.Value?.ToString(), negated: true),
            "IS NULL"    => rowValue is null,
            "IS NOT NULL" => rowValue is not null,
            "IN"         => MatchIn(rowValue, filter.Value),
            _            => true
        };
    }

    private static int CompareValues(object? left, object? right)
    {
        if (left is null && right is null) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        if (TryToDouble(left, out var ld) && TryToDouble(right, out var rd))
            return ld.CompareTo(rd);

        return string.Compare(left.ToString(), right?.ToString(), StringComparison.Ordinal);
    }

    private static bool TryToDouble(object? value, out double result)
    {
        result = 0;
        return value is not null && double.TryParse(value.ToString(), out result);
    }

    private static bool MatchLike(string? value, string? pattern, bool negated)
    {
        if (value is null || pattern is null)
            return negated ? value is not null : false;

        var regexPattern = "^" +
            System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("%", ".*")
                .Replace("_", ".") + "$";

        var matched = System.Text.RegularExpressions.Regex.IsMatch(
            value, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return negated ? !matched : matched;
    }

    private static bool MatchIn(object? value, object? inList)
    {
        if (inList is null) return false;

        if (inList is System.Collections.IEnumerable enumerable and not string)
            return enumerable.Cast<object?>().Any(v => CompareValues(value, v) == 0);

        return CompareValues(value, inList) == 0;
    }

    // ----------------------------------------------------------------
    // URL builder
    // ----------------------------------------------------------------

    private static string BuildUrl(
        string baseUrl,
        Dictionary<string, string> staticParams,
        Dictionary<string, string>? extraParams)
    {
        var allParams = new Dictionary<string, string>(staticParams, StringComparer.OrdinalIgnoreCase);
        if (extraParams is not null)
            foreach (var (k, v) in extraParams)
                allParams[k] = v;

        if (allParams.Count == 0)
            return baseUrl;

        var queryString = string.Join("&",
            allParams.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));

        var separator = baseUrl.Contains('?') ? "&" : "?";
        return baseUrl + separator + queryString;
    }

    // ----------------------------------------------------------------
    // Link header parser (RFC 5988) — TASK-017-04
    // ----------------------------------------------------------------

    /// <summary>
    /// Extracts the rel="next" URL from an RFC 5988 Link header.
    /// Format: &lt;https://api.example.com/items?page=2&gt;; rel="next", &lt;...&gt;; rel="last"
    /// </summary>
    private static string? ExtractNextLink(HttpResponseHeaders headers, string linkHeaderName)
    {
        if (!headers.TryGetValues(linkHeaderName, out var values))
            return null;

        foreach (var headerValue in values)
        {
            foreach (var entry in headerValue.Split(','))
            {
                var parts = entry.Split(';');
                if (parts.Length < 2) continue;

                var urlPart = parts[0].Trim();
                var relPart = parts.Skip(1)
                    .FirstOrDefault(p => p.Trim().StartsWith("rel=", StringComparison.OrdinalIgnoreCase));

                if (relPart is null) continue;

                var relValue = relPart.Trim()[4..].Trim().Trim('"', '\'');
                if (!string.Equals(relValue, "next", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (urlPart.StartsWith('<') && urlPart.EndsWith('>'))
                    return urlPart[1..^1];
            }
        }

        return null;
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static bool IsTransientHttpError(System.Net.HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode == System.Net.HttpStatusCode.TooManyRequests || (code >= 500 && code < 600);
    }

    private async Task DelayRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var delayMs = _options.BaseRetryDelaySeconds * 1000 * (int)Math.Pow(2, attempt - 1);
        _logger.LogDebug(
            "RestApiConnector: backing off {DelayMs}ms before retry attempt {Next}",
            delayMs, attempt + 1);
        await Task.Delay(delayMs, cancellationToken);
    }

    private static string InferFieldType(object? value) => value switch
    {
        null        => "nvarchar",
        bool        => "bit",
        long        => "bigint",
        int         => "int",
        double      => "float",
        float       => "float",
        decimal     => "decimal",
        DateTime    => "datetime",
        DateTimeOffset => "datetime",
        _           => "nvarchar"
    };
}
