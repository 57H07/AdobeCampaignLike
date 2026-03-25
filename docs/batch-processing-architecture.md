# Batch Processing Architecture Guide

## Overview

CampaignEngine uses a **Chunk Coordinator pattern** to process large campaigns in parallel. This approach splits a campaign's recipient list into fixed-size chunks, then enqueues one Hangfire background job per chunk. The last chunk to complete automatically triggers campaign finalization.

**Why not Hangfire Batches?** Hangfire Batch primitives (batch enqueue, continuation on all-complete) require the **Hangfire Pro** paid edition. The Chunk Coordinator pattern replaces that with atomic SQL counters and a completion-detection query — providing equivalent semantics with the Community (free) edition.

---

## Architecture Diagram

```
Campaign Operator
       |
       v
 [ChunkCoordinatorService]
  StartCampaignStepAsync()
       |
       +-- 1. Query recipients from DataSource
       +-- 2. RecipientChunkingService.Split() → N chunks
       +-- 3. Persist N CampaignChunk rows (status=Pending)
       +-- 4. Enqueue N Hangfire jobs (one per chunk)
       |
       v
 Hangfire Workers (up to 8 parallel)
       |
       v
 [ProcessChunkJob.ExecuteAsync()]   (runs per chunk)
  For each recipient:
    - Render template snapshot
    - Dispatch via channel (email/SMS)
    - Count success/failure
       |
       v
 [ChunkCoordinatorService]
  RecordChunkCompletionAsync()
    - UPDATE Campaigns SET ProcessedCount += N  (atomic SQL)
    - COUNT remaining Pending/Processing chunks
    - If 0 remaining → FinalizeStepAsync()
       |
       v
 [CampaignCompletionService]
  FinalizeStepAsync()
    - Evaluate failure rate
    - Set final Campaign.Status
```

---

## Key Components

### RecipientChunkingService

**Location:** `src/CampaignEngine.Infrastructure/Campaigns/RecipientChunkingService.cs`

Splits a flat list of recipient rows into fixed-size slices. Each slice becomes a `RecipientChunk` record with:

| Field | Description |
|---|---|
| `ChunkIndex` | Zero-based position in the sequence |
| `TotalChunks` | Total number of chunks for this split (used for completion detection) |
| `Recipients` | The subset of recipient data dictionaries |

**Algorithm:** Sequential partitioning using `Skip/Take`. No randomisation — recipients are processed in query order.

**Limits:**
- Minimum chunk size: 1 (clamped from configured value)
- Maximum chunk size: 10,000 (clamped from configured value)
- Empty list: returns a single empty chunk (guarantees at least one job is enqueued)

### ChunkCoordinatorService

**Location:** `src/CampaignEngine.Infrastructure/Campaigns/ChunkCoordinatorService.cs`

Orchestrates the full batch lifecycle. Three primary operations:

#### StartCampaignStepAsync

1. Loads the campaign and target step.
2. Queries recipients from the data source via `IDataSourceConnector`.
3. Splits recipients using `RecipientChunkingService.Split()`.
4. Persists all `CampaignChunk` rows in a single transaction.
5. After commit, enqueues one Hangfire job per chunk via `IBackgroundJobClient.Enqueue<IProcessChunkJob>`.
6. Stores the Hangfire job ID on each chunk for dashboard traceability.

**Why enqueue after commit?** Ensures jobs are only enqueued if the database write succeeded. Jobs may execute before the outer `SaveChangesAsync` for the job IDs, which is acceptable (the job ID is informational only).

#### RecordChunkCompletionAsync

Called by each `ProcessChunkJob` after it finishes (success or failure).

Uses a raw SQL `UPDATE...SET` to atomically increment the `Campaigns` table counters:

```sql
UPDATE Campaigns SET
    ProcessedCount = ProcessedCount + @processed,
    SuccessCount   = SuccessCount   + @success,
    FailureCount   = FailureCount   + @failure,
    UpdatedAt      = GETUTCDATE()
WHERE Id = @campaignId
```

This avoids EF Core read-modify-write races when multiple worker threads update the same campaign row concurrently.

After the update, queries the count of remaining `Pending` or `Processing` chunks for the step. If zero remain, this is the last chunk — `CampaignCompletionService.FinalizeStepAsync()` is called.

#### RecordChunkFailureAsync

Called when a `ProcessChunkJob` encounters an unhandled exception.

| Attempt | Action |
|---|---|
| attempts < MaxRetryAttempts | Increment `RetryAttempts`, set `Status = Pending`, schedule retry via `BackgroundJob.Schedule` with exponential delay |
| attempts >= MaxRetryAttempts | Set `Status = Failed`, record `CompletedAt`, then call `RecordChunkCompletionAsync(0, recipientCount)` to count all recipients as failures and check for campaign completion |

Default retry delays (configurable): 30s, 120s, 600s.

### ProcessChunkJob

**Location:** `src/CampaignEngine.Infrastructure/Batch/ProcessChunkJob.cs`

Hangfire job class. Decorated with:
- `[AutomaticRetry(Attempts = 0)]` — Hangfire's built-in retry is disabled; retries are managed by `ChunkCoordinatorService` to allow tracking in the database.
- `[DisableConcurrentExecution(3600)]` — prevents the same chunk from running twice concurrently (e.g., after a server restart).

**Execution flow per chunk:**
1. Load `CampaignChunk` with navigation properties (`CampaignStep`, `TemplateSnapshot`).
2. Validate: skip if already in terminal state (`Completed` or `Failed`).
3. Set `Status = Processing`, persist.
4. Deserialize `RecipientDataJson`.
5. For each recipient: render template, resolve address, dispatch via `ILoggingDispatchOrchestrator`.
6. Call `RecordChunkCompletionAsync(successCount, failureCount)`.

### CampaignCompletionService

**Location:** `src/CampaignEngine.Infrastructure/Batch/CampaignCompletionService.cs`

Determines the final campaign status based on the failure rate across all chunks:

| Failure Rate | Final Status |
|---|---|
| < 2% | `Completed` |
| >= 2% and < 10% | `PartialFailure` |
| >= 10% | `ManualReview` |

---

## Configuration

All batch processing parameters are configured under the `CampaignEngine:BatchProcessing` section in `appsettings.json`:

```json
{
  "CampaignEngine": {
    "BatchProcessing": {
      "ChunkSize": 500,
      "WorkerCount": 8,
      "MaxRetryAttempts": 3,
      "RetryDelaysSeconds": [30, 120, 600]
    }
  },
  "Hangfire": {
    "DashboardPath": "/hangfire",
    "WorkerCount": 8
  }
}
```

| Parameter | Default | Description |
|---|---|---|
| `ChunkSize` | 500 | Recipients per chunk. Range: 1–10,000. |
| `WorkerCount` | 8 | Hangfire parallel worker threads. |
| `MaxRetryAttempts` | 3 | Maximum retry attempts per chunk before permanent failure. |
| `RetryDelaysSeconds` | [30, 120, 600] | Delay in seconds before each retry attempt. |

---

## Database Schema

### CampaignChunks Table

Created by migration `20260325000001_AddCampaignChunks`.

| Column | Type | Description |
|---|---|---|
| `Id` | `uniqueidentifier` | Primary key (GUID) |
| `CampaignId` | `uniqueidentifier` | FK to Campaigns |
| `CampaignStepId` | `uniqueidentifier` | FK to CampaignSteps |
| `ChunkIndex` | `int` | Zero-based chunk position |
| `TotalChunks` | `int` | Total chunks for this step execution |
| `RecipientCount` | `int` | Number of recipients in this chunk |
| `RecipientDataJson` | `nvarchar(max)` | Serialized recipient data (JSON array) |
| `Status` | `int` | Enum: Pending=1, Processing=2, Completed=3, Failed=4 |
| `ProcessedCount` | `int` | Total recipients processed |
| `SuccessCount` | `int` | Successfully dispatched |
| `FailureCount` | `int` | Failed dispatch attempts |
| `RetryAttempts` | `int` | Number of retry attempts so far |
| `StartedAt` | `datetime2` | When the job started processing |
| `CompletedAt` | `datetime2` | When the chunk reached terminal status |
| `HangfireJobId` | `nvarchar(200)` | Hangfire job ID for dashboard traceability |
| `ErrorMessage` | `nvarchar(2000)` | Last error message (truncated to 2000 chars) |
| `CreatedAt` | `datetime2` | Audit timestamp |
| `UpdatedAt` | `datetime2` | Audit timestamp |

**Indexes:**
- `IX_CampaignChunks_CampaignId_StepId` — for progress queries by campaign and step
- `IX_CampaignChunks_StepId_Status` — for completion detection query

### Campaign Progress Columns

The `Campaigns` table has four atomic counters updated by `RecordChunkCompletionAsync`:

| Column | Description |
|---|---|
| `TotalRecipients` | Set at `StartCampaignStepAsync` time from data source query |
| `ProcessedCount` | Atomically incremented as each chunk completes |
| `SuccessCount` | Atomically incremented with successful dispatches |
| `FailureCount` | Atomically incremented with failed dispatches |

---

## Hangfire Dashboard

The Hangfire dashboard is available at `/hangfire` (configurable via `Hangfire:DashboardPath`).

**Access control:** Restricted to users in the `Admin` role. Configured in `Program.cs`:

```csharp
app.UseHangfireDashboard(hangfireOptions.DashboardPath, new DashboardOptions
{
    Authorization = [new HangfireAdminAuthorizationFilter()]
});
```

The dashboard shows:
- All enqueued, processing, succeeded, and failed jobs
- Hangfire job IDs (matching `CampaignChunk.HangfireJobId` for traceability)
- Retry history per job
- Server count and worker utilization

---

## Performance Characteristics

### Theoretical throughput

With 8 workers, chunk size 500, and 100ms per recipient dispatch:

| Metric | Value |
|---|---|
| Recipients per second per worker | 10 |
| Total recipients per second (8 workers) | 80 |
| Time for 100K recipients | ~21 minutes |

With faster dispatch (e.g., 50ms per recipient) and batched SMTP:

| Recipients per second per worker | 20 |
|---|---|
| Total per second (8 workers) | 160 |
| Time for 100K recipients | ~10.5 minutes |

Both scenarios are well within the 60-minute SLA target.

### Bottlenecks

1. **SMTP rate limits** — most SMTP providers enforce hourly or per-second sending limits. The `ChannelThrottleOptions` (default: 100/s for email) controls the application-side rate.
2. **Data source query** — querying 100K recipient rows from SQL Server should complete in under 5 seconds with proper indexes.
3. **RecipientDataJson size** — each chunk of 500 recipients serializes to approximately 25–100 KB depending on field count. Total for 200 chunks: 5–20 MB stored in the database.

### Scaling out

To increase throughput beyond 8 workers:
1. Increase `CampaignEngine:BatchProcessing:WorkerCount` and `Hangfire:WorkerCount`.
2. Deploy multiple Hangfire server instances (all pointing to the same SQL Server Hangfire storage).
3. The atomic SQL counter in `RecordChunkCompletionAsync` ensures exactly-once completion detection across all server instances.

---

## Failure Scenarios

### Single chunk failure

1. `ProcessChunkJob` catches an unhandled exception (or returns without calling `RecordChunkCompletionAsync`).
2. If `AutomaticRetry = 0` (current config), Hangfire marks the job as Failed without retrying.
3. The application-level retry is managed by `RecordChunkFailureAsync` — called explicitly by the job on handled errors.
4. After `MaxRetryAttempts`, the chunk is marked `Failed` and its recipients counted as failures in the campaign totals.

### Entire campaign stuck (no completion)

If a chunk job is lost (server crash before `RecordChunkCompletionAsync`), the campaign will never reach `Completed`. Mitigation:
- Monitor for campaigns in `Running` status after expected duration.
- Re-enqueue orphaned chunks (`Status = Processing` with old `StartedAt`) via an admin endpoint or scheduled cleanup job.
- The `DisableConcurrentExecution` attribute prevents the same chunk from running twice if the server recovers.

### SQL Server unavailability

All Hangfire jobs use the same SQL Server instance as the application database. If SQL Server is unavailable:
- Hangfire workers pause until connectivity is restored.
- Jobs are not lost — they remain in the Hangfire queue.
- Campaigns resume automatically when the server recovers.

---

## Monitoring

### Real-time progress

The `IChunkCoordinatorService.GetProgressAsync()` endpoint returns:

```json
{
  "campaignId": "...",
  "totalRecipients": 100000,
  "processedCount": 47500,
  "successCount": 47200,
  "failureCount": 300,
  "totalChunks": 200,
  "completedChunks": 95,
  "pendingChunks": 105,
  "failedChunks": 0,
  "status": "Running"
}
```

Poll this endpoint to display a progress bar.

### Logging

All batch operations emit structured logs with campaign and chunk IDs:

```
[INF] Campaign {CampaignId} Step {StepId}: splitting {RecipientCount} recipients into {ChunkCount} chunks of {ChunkSize}
[INF] Campaign {CampaignId} Step {StepId}: enqueued {ChunkCount} Hangfire jobs
[INF] ProcessChunkJob: starting chunk {ChunkId}
[INF] ProcessChunkJob: Chunk {ChunkId} finished. Success={Success}, Failed={Failed}
[INF] Campaign {CampaignId} Step {StepId}: all chunks completed — triggering finalization
[INF] Campaign {CampaignId} finalized. Status={Status}, Total={Total}, Success={Success}, Failed={Failed}
```

---

## Related Documentation

- [Campaign Creation Guide](campaign-creation-guide.md)
- [Database Schema and Migrations](database-schema-and-migrations.md)
- [Logging Conventions](logging-conventions.md)
- [Channel Dispatcher Extension Guide](channel-dispatcher-extension-guide.md)
