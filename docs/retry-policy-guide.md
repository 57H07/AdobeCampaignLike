# Retry Policy Guide

> US-035: Automatic retry for transient failures with exponential backoff.

## Overview

The CampaignEngine retry mechanism automatically recovers from transient dispatch failures
without manual intervention. It applies at two levels:

1. **Send-level retry** — individual recipient sends (handled by `IRetryPolicy`).
2. **Chunk-level retry** — entire Hangfire chunk jobs (handled by `[AutomaticRetry]`).

---

## Business Rules

| Rule | Description |
|------|-------------|
| BR-1 | Maximum **3 retry attempts** per send (configurable). |
| BR-2 | Exponential backoff delays: **30s, 2min (120s), 10min (600s)**. |
| BR-3 | Only **transient** failures are retried; permanent failures are not. |
| BR-4 | Retry count is tracked in the `RetryCount` column of `SEND_LOG`. |
| BR-5 | Retry success target: >90% within 3 attempts. |

---

## Send-Level Retry (IRetryPolicy)

### Architecture

```
LoggingDispatchOrchestrator
  └── IRetryPolicy.ExecuteAsync(operation, onRetry)
        ├── Attempt 0 (initial) — dispatches via IChannelDispatcher
        ├── [transient failure] → waits 30s → Attempt 1
        ├── [transient failure] → waits 2min → Attempt 2
        └── [transient failure] → waits 10min → Attempt 3 (final)
```

### Key Interfaces

**`IRetryPolicy`** (Application layer):
```csharp
public interface IRetryPolicy
{
    int MaxAttempts { get; }
    TimeSpan GetDelay(int attemptNumber);
    bool ShouldRetry(DispatchResult result, int currentRetryCount);
    Task<DispatchResult> ExecuteAsync(
        Func<int, CancellationToken, Task<DispatchResult>> operation,
        Func<DispatchResult, int, TimeSpan, Task>? onRetry = null,
        CancellationToken cancellationToken = default);
}
```

**`ITransientFailureDetector`** (Application layer):
```csharp
public interface ITransientFailureDetector
{
    bool IsTransient(Exception exception);
    bool IsTransientMessage(string errorMessage);
}
```

### Retry Execution Flow

The `LoggingDispatchOrchestrator.SendWithLoggingAsync` method manages the full lifecycle:

1. **Log Pending** — record the send attempt in `SEND_LOG` before dispatch.
2. **Execute with retry** — call `IRetryPolicy.ExecuteAsync`.
3. **On transient failure** — `onRetry` callback updates `SEND_LOG` status to `Retrying` with incremented `RetryCount`.
4. **After all retries** — final status written to `SEND_LOG`:
   - `Sent` on success.
   - `Failed` on permanent failure or after all retries exhausted.

### SEND_LOG Status Transitions

```
Pending → Retrying (retry 1)
        → Retrying (retry 2)
        → Retrying (retry 3)
        → Sent           (if a retry succeeds)
        → Failed         (after all retries exhausted)
```

```
Pending → Failed         (permanent failure — no retries)
```

---

## Transient vs. Permanent Failure Classification

The `TransientFailureDetector` classifies failures to determine retry eligibility.

### Transient Failures (Retriable)

| Category | Examples |
|----------|---------|
| SMTP connection | `SmtpDispatchException(isTransient: true)` — 4xx response codes, connection timeout |
| SMS rate limit | `SmsDispatchException(isTransient: true, httpStatusCode: 429)` |
| SMS server error | HTTP 500, 502, 503, 504 |
| Network errors | `SocketException`, `IOException` |
| Unknown exceptions | Unrecognised exceptions default to transient for recovery safety |

### Permanent Failures (Not Retriable)

| Category | Examples |
|----------|---------|
| Invalid recipient | `SmtpDispatchException(isTransient: false)` — 5xx reject (e.g. 550 Mailbox not found) |
| SMTP auth failure | Authentication failure |
| Invalid phone number | `InvalidPhoneNumberException` — E.164 format violation |
| Attachment error | `AttachmentValidationException` — size or type limit exceeded |
| Template error | `TemplateRenderException` — Scriban parse/render failure |
| Domain invariants | Any `DomainException` — must be fixed before retrying |
| Cancellations | `OperationCanceledException` — intentional, not retried |

### Error Message Classification

When the original exception is unavailable (dispatchers that return error strings),
`ITransientFailureDetector.IsTransientMessage` classifies by message content:

**Transient fragments**: `timeout`, `timed out`, `connection refused`, `connection reset`,
`temporarily unavailable`, `rate limit`, `too many requests`, `service unavailable`,
`try again`, `transient`, `socket`, `network`, `io error`.

**Permanent fragments** (checked first): `invalid email`, `invalid address`,
`authentication failed`, `authentication failure`, `invalid phone`, `not in e.164`,
`template error`, `template rendering`, `attachment`.

---

## Chunk-Level Retry (Hangfire AutomaticRetry)

Entire chunk jobs are automatically retried by Hangfire on infrastructure failures
(e.g. database connectivity, job queue issues).

```csharp
[AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
public sealed class ProcessChunkJob : IProcessChunkJob
{
    // ...
}
```

- **3 attempts** at the chunk level.
- After all attempts are exhausted the job is **deleted** from the failed queue.
- Hangfire manages its own backoff schedule independently of `IRetryPolicy`.
- Send-level retries (via `IRetryPolicy`) happen **within** each chunk job attempt.

---

## Configuration

Retry behaviour is configurable in `appsettings.json` under the `CampaignEngine:BatchProcessing` section:

```json
{
  "CampaignEngine": {
    "BatchProcessing": {
      "MaxRetryAttempts": 3,
      "RetryDelaysSeconds": [30, 120, 600]
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxRetryAttempts` | `3` | Maximum number of retry attempts per individual send. |
| `RetryDelaysSeconds` | `[30, 120, 600]` | Delay in seconds before each retry attempt (1-indexed). |

To disable retries entirely, set `MaxRetryAttempts` to `0`.

---

## DI Registration

Both services are registered in `ServiceCollectionExtensions.AddInfrastructure()`:

```csharp
// Stateless, configuration-driven — registered as Singleton
services.AddSingleton<IRetryPolicy, RetryPolicy>();

// Stateless classifier — registered as Singleton
services.AddSingleton<ITransientFailureDetector, TransientFailureDetector>();

// Scoped: wraps dispatchers with SEND_LOG lifecycle + retry
services.AddScoped<ILoggingDispatchOrchestrator, LoggingDispatchOrchestrator>();
```

---

## Testing

Unit tests are in `tests/CampaignEngine.Application.Tests/SendLogs/`:

| Test File | Coverage |
|-----------|---------|
| `RetryPolicyTests.cs` | Backoff delays, ShouldRetry logic, ExecuteAsync execution flow, onRetry callbacks, cancellation |
| `TransientFailureDetectorTests.cs` | SMTP/SMS exception classification, error message fragments, edge cases |
| `LoggingDispatchOrchestratorTests.cs` | Full SEND_LOG lifecycle integration with retry |

### Example: Testing with a Zero-Delay Policy

For fast unit tests, configure retry delays as `[0, 0, 0]`:

```csharp
var policy = new RetryPolicy(Options.Create(new CampaignEngineOptions
{
    BatchProcessing = new BatchProcessingOptions
    {
        MaxRetryAttempts = 3,
        RetryDelaysSeconds = [0, 0, 0]  // No actual waiting in tests
    }
}));
```
