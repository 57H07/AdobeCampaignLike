# REST API Connector Configuration Guide

**Epic:** Data Source Connector (US-017)
**Roles required:** Admin (configure connectors), Operator (use in campaigns)

---

## Overview

The REST API connector (`RestApiConnector`) implements `IDataSourceConnector` using
`HttpClient` to consume recipient data from external REST API endpoints.
It enables Campaign Operators to target recipient populations served by any HTTP-accessible
JSON API — CRM endpoints, marketing platforms, external data services — without writing code.

Key design decisions:

| Concern | Implementation |
|---------|----------------|
| API timeout | 60 seconds per request (configurable) |
| Retry policy | Up to 3 attempts with exponential backoff on 5xx / 429 / timeout |
| Response size guard | 50 MB maximum response body (streaming check via `LimitedStream`) |
| JSON data path | Dot-notation path to the array inside the response body |
| Authentication | None, API Key header, Bearer token, OAuth2 client credentials |
| Pagination | None (single page), page-param (?page=N), Link-header rel="next" |
| Filtering | Applied in-memory after fetching all data |
| Schema discovery | Inferred from first JSON record fields and value types |

---

## Connection String Format

The REST API connector uses a **semicolon-delimited Key=Value** connection string
(similar to ADO.NET connection strings). All keys are case-insensitive.

```
Url=<endpoint>;[Auth=<mode>;]...[Pagination options]...[DataPath=<path>]
```

### Core Settings

| Key | Description | Example |
|-----|-------------|---------|
| `Url` | **Required.** Base URL of the API endpoint | `https://api.example.com/recipients` |
| `DataPath` | Dot-notation path to the data array in the response | `data` or `response.data.items` |
| `QueryParams` | Comma-separated static query parameters appended to every request | `format=json,version=2` |

### Authentication Settings

| Key | Description |
|-----|-------------|
| `Auth` | Authentication mode: `None` (default), `ApiKey`, `Bearer`, `OAuth2` |
| `ApiKeyHeader` | Header name for API key auth (default: `X-Api-Key`) |
| `ApiKeyValue` | API key value |
| `BearerToken` | Static bearer token for `Bearer` mode |
| `OAuth2TokenUrl` | Token endpoint URL for `OAuth2` mode |
| `OAuth2ClientId` | OAuth2 client ID |
| `OAuth2ClientSecret` | OAuth2 client secret |
| `OAuth2Scope` | OAuth2 scope (optional) |

### Pagination Settings

| Key | Description | Default |
|-----|-------------|---------|
| `PageParam` | Query parameter name for page number | `page` |
| `PageSizeParam` | Query parameter name for page size | `per_page` |
| `PageSize` | Number of items per page | `100` |
| `FirstPageIndex` | Index of the first page (0 or 1) | `1` |
| `TotalPagesHeader` | Response header name containing total page count | *(none)* |
| `NextLinkHeader` | Response header name for Link-style pagination | `Link` |

When `PageParam` is set, page-param pagination is activated.
When `NextLinkHeader` is set, Link-header pagination is activated (takes precedence).
When neither is set, a single request is made.

---

## Connection String Examples

### No authentication, single page

```
Url=https://api.example.com/recipients
```

### API Key authentication

```
Url=https://api.example.com/recipients;Auth=ApiKey;ApiKeyHeader=X-Api-Key;ApiKeyValue=your-key-here
```

### Bearer token

```
Url=https://api.example.com/recipients;Auth=Bearer;BearerToken=eyJhbGciOiJSUzI1NiJ9...
```

### OAuth2 client credentials

```
Url=https://api.example.com/recipients;
Auth=OAuth2;
OAuth2TokenUrl=https://auth.example.com/oauth/token;
OAuth2ClientId=campaignengine;
OAuth2ClientSecret=secret;
OAuth2Scope=read:recipients
```

### Page-param pagination with nested data path

```
Url=https://api.example.com/contacts;
DataPath=data;
PageParam=page;PageSizeParam=limit;PageSize=200;
TotalPagesHeader=X-Total-Pages
```

### Link-header pagination

```
Url=https://api.github.com/orgs/myorg/members;
Auth=Bearer;BearerToken=ghp_mytoken;
NextLinkHeader=Link
```
The connector follows `Link: <url>; rel="next"` headers (RFC 5988) until absent.

### Static query parameters

```
Url=https://api.example.com/recipients;QueryParams=status=active,list_id=42
```

---

## JSON Data Path

The `DataPath` setting selects the array of records from the JSON response using
dot-notation (e.g., `data`, `response.data.items`).

| Response shape | DataPath | Behavior |
|----------------|----------|----------|
| `[{...},{...}]` | *(empty)* | Root is already an array |
| `{"data":[{...}]}` | `data` | Extracts `data` array |
| `{"meta":{...},"results":{"items":[{...}]}}` | `results.items` | Deep navigation |
| `{"id":1,"name":"Alice"}` | *(empty)* | Single object → one-row list |

If the path is not found, the connector falls back to the last element it reached
and attempts to parse it (graceful degradation).

---

## JSON to Data Row Mapping

JSON values are mapped to .NET types as follows:

| JSON type | .NET type |
|-----------|-----------|
| string | `string` |
| integer number | `long` |
| fractional number | `double` |
| boolean | `bool` |
| null | `null` |
| object or array | JSON string (serialized for flat-row compatibility) |

Field names are **case-insensitive** in the resulting row dictionaries.

---

## Business Rules

| Rule | Value |
|------|-------|
| Request timeout | 60 seconds (configurable via `RestApiConnector:TimeoutSeconds`) |
| Retry attempts | 3 (configurable via `RestApiConnector:MaxRetryAttempts`) |
| Retry backoff | Exponential: 1s, 2s, 4s (base configurable via `BaseRetryDelaySeconds`) |
| Transient errors retried | 5xx, 429 Too Many Requests, `HttpRequestException`, timeout |
| Maximum response body | 50 MB (configurable via `RestApiConnector:MaxResponseSizeBytes`) |
| Maximum pages fetched | 1000 (configurable via `RestApiConnector:MaxPages`) |

---

## appsettings.json Configuration

```json
{
  "RestApiConnector": {
    "TimeoutSeconds": 60,
    "MaxRetryAttempts": 3,
    "BaseRetryDelaySeconds": 1,
    "MaxResponseSizeBytes": 52428800,
    "MaxPages": 1000
  }
}
```

---

## Filtering

REST API data sources support all filter operators supported by the SQL Server connector:
`=`, `!=`, `>`, `<`, `>=`, `<=`, `LIKE`, `NOT LIKE`, `IN`, `IS NULL`, `IS NOT NULL`.

Filtering is applied **in-memory** after fetching all pages, because REST APIs do not
generally expose SQL-style server-side filtering. For large datasets, consider:

- Defining server-side filters as static query parameters (`QueryParams`) if the API supports them.
- Using `DataPath` to select a pre-filtered sub-array from the response.
- Fetching with pagination and relying on the connector's in-memory filter for final narrowing.

---

## Authentication: OAuth2 Token Caching

When `Auth=OAuth2` is configured, the connector uses `RestApiOAuth2TokenCache` to:

- Fetch an access token from the `OAuth2TokenUrl` using the client credentials flow.
- Cache the token in memory until it expires (with a 30-second safety margin).
- Automatically refresh the token when it is near expiry or has expired.
- Use a `SemaphoreSlim` to prevent thundering-herd on token refresh.

The token cache is registered as a **Singleton** so tokens persist across scoped
connector instances within the same process lifetime.

---

## Connector Registry

Both `SqlServerConnector` and `RestApiConnector` are registered in
`DataSourceConnectorRegistry` (implementing `IDataSourceConnectorRegistry`).
The registry resolves the correct connector at runtime based on `DataSource.Type`:

| DataSourceType | Connector |
|----------------|-----------|
| `SqlServer` | `SqlServerConnector` |
| `RestApi` | `RestApiConnector` |

Services that consume data sources (`DataSourcePreviewService`,
`TemplatePreviewService`, `RecipientCountService`, `ChunkCoordinatorService`)
inject `IDataSourceConnectorRegistry` and call `GetConnector(dataSource.Type)`.

---

## Adding a New Connector

To extend the system with an additional connector type (e.g., CSV, SFTP):

1. Add a new value to `DataSourceType` enum (Domain layer).
2. Implement `IDataSourceConnector` in the Infrastructure layer.
3. Register the implementation in `ServiceCollectionExtensions.cs`.
4. Add the new `DataSourceType` → connector mapping in `DataSourceConnectorRegistry`.

No changes are required in any service or controller.

---

## Security Notes

- API keys and OAuth2 secrets are stored in the `EncryptedConnectionString` column
  of the `DataSources` table using ASP.NET Core Data Protection encryption.
- Keys are never exposed in logs or API responses.
- The connector does not follow HTTP redirects to different hosts automatically.
- Response size is enforced via `LimitedStream` to prevent memory exhaustion attacks.
