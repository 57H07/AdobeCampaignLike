# API Rate Limiting Guide

## Overview

CampaignEngine enforces **per-API-key rate limiting** to prevent abuse and ensure fair resource
allocation across all consumers.

Every request authenticated via an API key is subject to a configurable rate limit.
Requests that exceed the limit receive a `429 Too Many Requests` response immediately.

---

## Business Rules

| Rule | Value |
|------|-------|
| Default rate limit | 1000 requests/minute |
| Window algorithm | Sliding 1-minute window |
| Rate limit response | `429 Too Many Requests` |
| Configurable per key | Yes — overrides the default |
| Headers on every response | `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset` |

---

## Response Headers

Every API-key-authenticated response includes the following headers:

| Header | Type | Description |
|--------|------|-------------|
| `X-RateLimit-Limit` | Integer | The configured limit for this key (requests/minute). |
| `X-RateLimit-Remaining` | Integer | Remaining requests in the current window. |
| `X-RateLimit-Reset` | Unix timestamp | Seconds since epoch when the current window resets. |

Example response headers for an allowed request:

```
HTTP/1.1 200 OK
X-RateLimit-Limit: 1000
X-RateLimit-Remaining: 742
X-RateLimit-Reset: 1743200460
Content-Type: application/json
```

---

## Rate Limit Exceeded (429)

When the limit is exceeded, the response is:

```
HTTP/1.1 429 Too Many Requests
Retry-After: 37
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 0
X-RateLimit-Reset: 1743200497
Content-Type: application/json

{
  "status": 429,
  "title": "Too Many Requests",
  "detail": "Rate limit exceeded. Maximum 100 requests per minute per API key.",
  "retryAfterSeconds": 37
}
```

The `Retry-After` header gives the number of seconds until the window resets.
Clients should respect this value and delay their next retry accordingly.

---

## Configuring Rate Limits

### Default rate limit (system-wide)

The default rate limit applies to all API keys that do not have a per-key override.
Configure it in `appsettings.json`:

```json
{
  "ApiKey": {
    "DefaultRateLimitPerMinute": 1000
  }
}
```

### Per-key rate limit at creation

Set a custom rate limit when creating an API key:

```http
POST /api/apikeys
Content-Type: application/json
X-Api-Key: <admin-key>

{
  "name": "High-volume integration",
  "rateLimitPerMinute": 5000
}
```

### Updating an existing key's rate limit

Use the `PATCH /api/apikeys/{id}/rate-limit` endpoint to change the limit after creation:

```http
PATCH /api/apikeys/3fa85f64-5717-4562-b3fc-2c963f66afa6/rate-limit
Content-Type: application/json
X-Api-Key: <admin-key>

{
  "rateLimitPerMinute": 2000
}
```

Pass `null` to reset to the system default:

```http
PATCH /api/apikeys/3fa85f64-5717-4562-b3fc-2c963f66afa6/rate-limit
Content-Type: application/json
X-Api-Key: <admin-key>

{
  "rateLimitPerMinute": null
}
```

The new limit takes effect on the **next request** from that key.

### Admin UI

Rate limits are also configurable through the Admin UI:

- **Create key**: Rate limit field on the Create API Key form (`/Admin/ApiKeys/Create`).
- **Edit existing**: Click **Rate Limit** button next to an active key on the API Keys list
  (`/Admin/ApiKeys/{id}/EditRateLimit`).

---

## Monitoring and Alerting

### Rate limit statistics endpoint

The `GET /api/apikeys/rate-limit-stats` endpoint returns per-key usage statistics:

```http
GET /api/apikeys/rate-limit-stats
X-Api-Key: <admin-key>
```

Response:

```json
[
  {
    "apiKeyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "limitPerMinute": 1000,
    "totalRequests": 15432,
    "totalRejected": 12,
    "requestsInCurrentWindow": 87,
    "remainingInCurrentWindow": 913,
    "currentWindowResetAt": "2026-03-28T14:02:00Z"
  }
]
```

| Field | Description |
|-------|-------------|
| `totalRequests` | Accepted requests since service start. |
| `totalRejected` | Rejected (rate-limit-exceeded) requests since service start. |
| `requestsInCurrentWindow` | Requests counted in the current sliding window (last 60 s). |
| `remainingInCurrentWindow` | Remaining quota in the current window. |
| `currentWindowResetAt` | UTC time when the oldest timestamp in the window expires. |

> Statistics are in-process (not persisted). They reset on service restart.

### Alert thresholds (recommended)

Monitor `totalRejected` per key over time. Consider alerting when:

- `totalRejected / (totalRequests + totalRejected) > 5%` — consumer may need a higher limit.
- `requestsInCurrentWindow / limitPerMinute > 90%` — consumer is close to the limit.

---

## Algorithm: Sliding Window

The rate limiter uses a **sliding 1-minute window** algorithm:

1. Each API key has an in-memory queue of accepted-request timestamps.
2. On each request, timestamps older than 60 seconds are evicted from the queue.
3. If the queue size is below the limit, the request is accepted and its timestamp is enqueued.
4. If the queue is at the limit, the request is rejected immediately (no queuing).

**Advantages over fixed window:**
- No "burst at boundary" effect where two full windows of requests fit in a 2-second span.
- `X-RateLimit-Reset` reflects the actual next available slot rather than an arbitrary window boundary.

**Trade-off:**
- State is in-process. For multi-instance deployments, use a distributed cache-backed implementation
  of `IApiKeyRateLimiter` (see `CampaignEngine.Application/Interfaces/IApiKeyRateLimiter.cs`).

---

## Integration Examples

### curl — check remaining quota

```bash
curl -I -X POST https://your-server/api/send \
  -H "X-Api-Key: ce_aBcDeFgH..." \
  -H "Content-Type: application/json" \
  -d '{ "templateId": "...", "channel": "Email", ... }'
```

Inspect the response headers:

```
X-RateLimit-Limit: 1000
X-RateLimit-Remaining: 741
X-RateLimit-Reset: 1743200460
```

### .NET HttpClient — handle 429 with retry

```csharp
public async Task<HttpResponseMessage> SendWithRateLimitRetryAsync(
    HttpClient client,
    HttpRequestMessage request,
    CancellationToken ct)
{
    var response = await client.SendAsync(request, ct);

    if ((int)response.StatusCode == 429)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.First(), out var waitSeconds))
        {
            await Task.Delay(TimeSpan.FromSeconds(waitSeconds + 1), ct);
        }
        // Recreate the request content before retrying (HttpContent is single-use)
        // ...
    }

    return response;
}
```

---

## Related Documentation

- [API Authentication Guide](api-authentication-guide.md) — API key creation, revocation, rotation.
- [Single Send API](single-send-api.md) — Send API endpoint reference.
- [Swagger/OpenAPI Guide](swagger-openapi-guide.md) — Interactive API documentation.
