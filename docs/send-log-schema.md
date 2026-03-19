# Send Log Schema Documentation

**User Story:** US-034 — Send logging and audit trail
**Table:** `SendLogs`
**Status:** Source of truth for all dispatch activity

---

## Overview

The `SEND_LOG` table records every send attempt made by CampaignEngine.
It is the authoritative audit trail for all dispatch operations across email, SMS, and letter channels.

**Business rules:**
1. Every send attempt is logged with `Pending` status **before** dispatch begins.
2. Status is updated to `Sent`, `Failed`, or `Retrying` **after** the dispatch result is known.
3. Error details are captured in the `ErrorDetail` column on all failure paths.
4. Retention: 90 days (configurable via scheduled cleanup job).

---

## Table Schema

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | `uniqueidentifier` | NOT NULL | Primary key (GUID, auto-generated) |
| `CampaignId` | `uniqueidentifier` | NOT NULL | FK to the `Campaigns` table |
| `CampaignStepId` | `uniqueidentifier` | NULL | FK to `CampaignSteps` (null for single-step campaigns) |
| `Channel` | `int` | NOT NULL | Channel enum: Email=1, Sms=2, Letter=3 |
| `Status` | `int` | NOT NULL | Send status (see table below) |
| `RecipientAddress` | `nvarchar(500)` | NOT NULL | Email address, phone (E.164), or display name |
| `RecipientId` | `nvarchar(200)` | NULL | External reference ID from the data source |
| `SentAt` | `datetime2` | NULL | UTC timestamp of successful dispatch (null if not yet sent) |
| `RetryCount` | `int` | NOT NULL | Number of retry attempts made (default: 0) |
| `ErrorDetail` | `nvarchar(max)` | NULL | Error message from the last failed dispatch attempt |
| `CorrelationId` | `nvarchar(100)` | NULL | Links this send to a campaign chunk or API request |
| `CreatedAt` | `datetime2` | NOT NULL | UTC timestamp when the log entry was created (= attempt start) |
| `UpdatedAt` | `datetime2` | NOT NULL | UTC timestamp of the last status update |

---

## Send Status Enum

| Value | Name | Description |
|-------|------|-------------|
| `1` | `Pending` | Logged before dispatch; awaiting send result |
| `2` | `Sent` | Message dispatched successfully |
| `3` | `Failed` | Permanent failure; no further retries |
| `4` | `Retrying` | Transient failure; retry scheduled |

---

## Database Indexes

| Index Name | Columns | Purpose |
|------------|---------|---------|
| `IX_SendLogs_CampaignId` | `CampaignId` | Filter logs by campaign |
| `IX_SendLogs_Status` | `Status` | Filter logs by status |
| `IX_SendLogs_CreatedAt` | `CreatedAt` | Filter/sort by date range |
| `IX_SendLogs_CorrelationId` | `CorrelationId` | Trace logs by correlation ID |

---

## Status Lifecycle

```
[API/Batch Job]
      |
      v
  LogPending()          -> Status = Pending, CreatedAt = now
      |
      v
[Channel Dispatcher]
      |
      +-- Success -----> LogSent()     -> Status = Sent,     SentAt = now
      |
      +-- Transient ---> LogRetrying() -> Status = Retrying, RetryCount++
      |       |
      |       +-- [retry attempt] --> LogPending() again (new entry)
      |
      +-- Permanent ---> LogFailed()   -> Status = Failed,   ErrorDetail = error message
```

---

## Key Services

### `ISendLogService` (Application layer interface)

| Method | Description |
|--------|-------------|
| `LogPendingAsync(...)` | Creates a new `Pending` entry before dispatch |
| `LogSentAsync(id, sentAt)` | Updates to `Sent` with `SentAt` timestamp |
| `LogFailedAsync(id, error, retryCount)` | Updates to `Failed` with error detail |
| `LogRetryingAsync(id, error, retryCount)` | Updates to `Retrying` with error detail |
| `QueryAsync(filters...)` | Paginated query with filtering |
| `CountAsync(filters...)` | Count matching entries |
| `GetByIdAsync(id)` | Fetch single entry by ID |

### `ILoggingDispatchOrchestrator` (Application layer interface)

Wraps `IChannelDispatcher.SendAsync()` with automatic before/after SEND_LOG recording.
Use this instead of calling dispatchers directly to ensure every send is logged.

```csharp
var (sendLogId, result) = await _orchestrator.SendWithLoggingAsync(
    request,
    correlationId: correlationId,
    currentRetryCount: retryCount);
```

---

## REST API

### `GET /api/sendlogs`

Returns a paginated list of send log entries. Requires Operator or Admin role.

**Query parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `campaignId` | `Guid?` | Filter by campaign ID |
| `recipient` | `string?` | Partial match on recipient address |
| `status` | `int?` | Filter by status (1=Pending, 2=Sent, 3=Failed, 4=Retrying) |
| `from` | `DateTime?` | Filter entries created on or after this UTC datetime |
| `to` | `DateTime?` | Filter entries created on or before this UTC datetime |
| `page` | `int` | Page number (1-based, default: 1) |
| `pageSize` | `int` | Page size (1–200, default: 50) |

**Response:**
```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "campaignId": "6e2fae47-...",
      "campaignStepId": null,
      "channel": "Email",
      "status": "Sent",
      "recipientAddress": "user@example.com",
      "recipientId": "EXT-001",
      "sentAt": "2026-03-19T10:32:15Z",
      "retryCount": 0,
      "errorDetail": null,
      "correlationId": "chunk-42-abc",
      "createdAt": "2026-03-19T10:32:14Z",
      "updatedAt": "2026-03-19T10:32:15Z"
    }
  ],
  "total": 1500,
  "page": 1,
  "pageSize": 50,
  "totalPages": 30
}
```

### `GET /api/sendlogs/{id}`

Returns a single send log entry by its GUID. Returns `404` if not found.

---

## UI

The send log viewer is available at `/Audit/SendLogs` and provides:

- **Search filters:** Campaign ID, recipient address, status dropdown, date range
- **Results table:** Paginated list with status badges, timestamps, retry counts, error previews
- **Detail view:** Full single-entry view at `/Audit/SendLogs/{id}` with error detail and quick-filter links
- **Access:** Operator and Admin roles only

---

## Retention Policy

The default retention period is 90 days. Entries older than the retention threshold can be purged
by a scheduled background job (not yet implemented). The retention window is configurable
via application settings:

```json
{
  "CampaignEngine": {
    "SendLog": {
      "RetentionDays": 90
    }
  }
}
```

---

## Example: Manual log query (SQL)

```sql
-- All failed sends for a specific campaign in the last 7 days
SELECT
    Id,
    RecipientAddress,
    Channel,
    Status,
    RetryCount,
    ErrorDetail,
    CreatedAt
FROM SendLogs
WHERE CampaignId = '6e2fae47-...'
  AND Status = 3           -- Failed
  AND CreatedAt >= DATEADD(day, -7, GETUTCDATE())
ORDER BY CreatedAt DESC;
```
