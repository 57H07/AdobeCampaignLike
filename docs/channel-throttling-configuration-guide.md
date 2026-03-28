# Channel Throttling and Rate Limiting Configuration Guide

**US-022** — Configurable per-channel send rate limiting (token bucket algorithm).

---

## Overview

CampaignEngine enforces per-channel rate limits to prevent overwhelming SMTP servers,
SMS provider APIs, or other downstream infrastructure. Rate limiting uses a **token bucket**
algorithm that:

- Allows **burst capacity** above the sustained rate (short-lived spikes).
- Applies **backpressure**: dispatch workers wait for tokens rather than dropping messages.
- Returns **transient failures** when the wait timeout elapses, triggering the standard
  retry policy (30s → 2min → 10min exponential backoff, US-035).

---

## Default Rates

| Channel | Default rate  | Burst capacity | Max wait |
|---------|--------------|----------------|----------|
| Email   | 100 msg/sec  | 200 tokens     | 30 sec   |
| SMS     | 10 msg/sec   | 20 tokens      | 10 sec   |
| Letter  | Unlimited    | N/A            | N/A      |

These defaults follow the business rules specified in US-022 (BR-1).

---

## Configuration

Rate limits are configured in `appsettings.json` under `CampaignEngine:RateLimit`:

```json
{
  "CampaignEngine": {
    "RateLimit": {
      "Email": {
        "TokensPerSecond": 100,
        "MaxWaitTimeSeconds": 30,
        "BurstMultiplier": 2.0
      },
      "Sms": {
        "TokensPerSecond": 10,
        "MaxWaitTimeSeconds": 10,
        "BurstMultiplier": 2.0
      },
      "Letter": {
        "TokensPerSecond": 0,
        "MaxWaitTimeSeconds": 0,
        "BurstMultiplier": 1.0
      }
    }
  }
}
```

### Settings Reference

| Setting             | Type    | Default | Description |
|---------------------|---------|---------|-------------|
| `TokensPerSecond`   | int     | 0       | Maximum sustained send rate in messages per second. **0 = unlimited.** |
| `MaxWaitTimeSeconds`| int     | 30      | How long a dispatch worker waits for a token before returning a transient error. 0 = wait indefinitely (not recommended). |
| `BurstMultiplier`   | double  | 2.0     | Bucket capacity multiplier. `capacity = TokensPerSecond * BurstMultiplier`. Allows short bursts. |

### Per-Environment Overrides

Use `appsettings.Production.json` to set production-specific limits:

```json
{
  "CampaignEngine": {
    "RateLimit": {
      "Email": {
        "TokensPerSecond": 50,
        "MaxWaitTimeSeconds": 60
      }
    }
  }
}
```

Use environment variables (for Docker/CI):

```
CampaignEngine__RateLimit__Email__TokensPerSecond=50
CampaignEngine__RateLimit__Email__MaxWaitTimeSeconds=60
```

---

## How Token Bucket Works

### Sustained rate

At 100 msg/sec with `BurstMultiplier=2.0`, the bucket holds 200 tokens.

- Tokens refill continuously at 100/sec.
- Each send consumes 1 token.
- Up to 200 sends can happen instantly if the bucket is full (burst).
- Once the burst is consumed, throughput is throttled to 100/sec.

```
Bucket full:   ████████████████████ 200 tokens
After 100 sends: ██████████ 100 tokens (100 refilled instantly from burst)
```

### Backpressure under load

When the bucket is exhausted:

1. The dispatch worker calls `WaitAsync()` and blocks.
2. Tokens are refilled at the configured rate.
3. As soon as a token becomes available, the worker proceeds.
4. Multiple concurrent workers queue fairly behind a lock.

### Rate limit exceeded

If the wait exceeds `MaxWaitTimeSeconds`:

1. `WaitAsync()` throws `OperationCanceledException`.
2. `ThrottledChannelDispatcher` catches it and returns `DispatchResult.Fail(isTransient: true)`.
3. The retry policy (US-035) retries after 30s, then 2min, then 10min.
4. A `RateLimitExceeded` metric is recorded.

---

## Architecture

```
ProcessChunkJob
  └── LoggingDispatchOrchestrator
        └── ChannelDispatcherRegistry
              └── ThrottledChannelDispatcher (decorator)
                    ├── IChannelRateLimiterRegistry.GetLimiter(channel)
                    │     └── TokenBucketRateLimiter.WaitAsync()
                    └── Inner dispatcher (EmailDispatcher / SmsDispatcher / LetterDispatcher)
```

The `ThrottledChannelDispatcher` is a transparent decorator registered in DI that wraps
each concrete dispatcher. The rate limiter registry is a **Singleton** — the token bucket
state persists across all concurrent requests, correctly enforcing the sustained rate
across all Hangfire workers.

---

## Monitoring Metrics

Rate limit metrics are exposed via a REST endpoint (Admin role required):

```
GET /api/admin/rate-limit-metrics
Authorization: X-Api-Key <admin-key>
```

**Example response:**

```json
{
  "generatedAtUtc": "2026-03-28T12:00:00Z",
  "channels": [
    {
      "channel": "Email",
      "configuredRatePerSecond": 100,
      "isThrottled": true,
      "tokensAcquired": 12500,
      "throttleWaitCount": 45,
      "totalWaitMs": 2250,
      "averageWaitMs": 50,
      "rateLimitExceededCount": 0,
      "currentSendRatePerSecond": 87.3,
      "windowStartUtc": "2026-03-28T11:00:00Z",
      "availableTokens": 132.4,
      "waitingCount": 2
    },
    ...
  ]
}
```

**Reset counters** (admin only):

```
POST /api/admin/rate-limit-metrics/reset
Authorization: X-Api-Key <admin-key>
```

### Field Reference

| Field                    | Description |
|--------------------------|-------------|
| `configuredRatePerSecond`| 0 = unlimited |
| `tokensAcquired`         | Total successful sends since last reset |
| `throttleWaitCount`      | Times a worker waited for a token (backpressure applied) |
| `totalWaitMs`            | Cumulative milliseconds spent waiting |
| `averageWaitMs`          | Average wait per throttle event |
| `rateLimitExceededCount` | Times the wait timeout was exceeded (triggers retry) |
| `currentSendRatePerSecond` | Approximate sustained rate since last reset |
| `availableTokens`        | Current bucket level. -1 = unlimited |
| `waitingCount`           | Dispatch workers currently waiting for a token |

---

## Tuning Guidelines

### SMTP (Email)

- Start with `TokensPerSecond: 100` (safe for most SMTP servers).
- Reduce to `50` if your SMTP provider reports rate-limit errors in send logs.
- `MaxWaitTimeSeconds: 30` is appropriate for batch campaigns; reduce to `10` for
  transactional sends where latency matters.

### SMS

- Set `TokensPerSecond` to your provider's contracted rate (e.g., Twilio: 1/sec per
  phone number, up to 100/sec on bulk short codes).
- If `rateLimitExceededCount` is growing, increase `MaxWaitTimeSeconds` or reduce the rate.

### Letter

- Leave `TokensPerSecond: 0` (unlimited). PDF generation is CPU-bound; the Hangfire
  worker count (`Hangfire:WorkerCount`) controls throughput naturally.

---

## Diagnosing Rate Limit Issues

1. Check `/api/admin/rate-limit-metrics` — look for `rateLimitExceededCount > 0`.
2. Search send logs (GET `/api/sendlogs?status=Failed`) for errors containing
   "Rate limit exceeded".
3. These failures have `IsTransient=true` and will be automatically retried.
4. If retries also fail, the SMTP/SMS provider may be enforcing server-side limits —
   reduce `TokensPerSecond` accordingly.

---

## See Also

- [Retry Policy Guide](retry-policy-guide.md) — how transient failures are retried
- [Batch Processing Architecture](batch-processing-architecture.md) — chunk dispatch pipeline
- [Send Log Schema](send-log-schema.md) — querying failed/retrying sends
