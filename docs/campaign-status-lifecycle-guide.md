# Campaign Status Lifecycle Guide

This document describes the campaign status lifecycle in CampaignEngine, covering all lifecycle states, allowed transitions, business rules for failure thresholds, and API usage for real-time monitoring.

## Overview

A campaign follows a strictly forward lifecycle from creation to completion:

```
  [New Campaign]
        |
        v
     DRAFT
        |
        | (schedule: ScheduledAt ≥ 5 min from now)
        v
   SCHEDULED
        |
        | (batch job starts processing)
        v
    RUNNING
        |
        | (first chunk dispatched)
        v
 STEP_IN_PROGRESS ──────────────────────────────> COMPLETED
        |                  (all chunks done,         (failure rate < 2%)
        |                   step complete)
        |                                      ───> PARTIAL_FAILURE
        v                                           (failure rate 2%–10%)
  WAITING_NEXT
        |                                      ───> MANUAL_REVIEW
        | (next step triggered)                     (failure rate ≥ 10%)
        v
 STEP_IN_PROGRESS (next step)
        ...
```

**Key rules:**
- New campaigns always start in **Draft** status
- Campaigns can only move forward — no backward transitions are permitted
- **PartialFailure** and **ManualReview** are set automatically based on send failure rates
- **ManualReview** requires Admin intervention before any further action

---

## Status Descriptions

| Status | Numeric Value | Description |
|---|---|---|
| `Draft` | 1 | Campaign has been created and is being configured. Not yet scheduled. |
| `Scheduled` | 2 | Campaign has been scheduled to run at a specific time. Template snapshots are frozen. |
| `Running` | 3 | The batch job has started; recipients are being loaded and chunk jobs are being enqueued. |
| `StepInProgress` | 4 | One or more dispatch chunks for the current step are actively being processed. |
| `WaitingNext` | 5 | The current step has completed. Waiting for the delay period before starting the next step. |
| `Completed` | 6 | All steps finished with a failure rate below 2%. Terminal state. |
| `PartialFailure` | 7 | All steps finished but more than 2% (and less than 10%) of sends failed. Terminal state. |
| `ManualReview` | 8 | More than 10% of sends failed. Requires Admin intervention. Terminal state. |

---

## Allowed Transitions

The `CampaignStatusService` enforces the following transition table:

| From | Allowed Targets |
|---|---|
| `Draft` | `Scheduled` |
| `Scheduled` | `Running` |
| `Running` | `StepInProgress` |
| `StepInProgress` | `WaitingNext`, `Completed`, `PartialFailure`, `ManualReview` |
| `WaitingNext` | `StepInProgress`, `Completed`, `PartialFailure`, `ManualReview` |
| `Completed` | *(none — terminal)* |
| `PartialFailure` | *(none — terminal)* |
| `ManualReview` | *(none — terminal)* |

Any transition not listed above is rejected. Use `ICampaignStatusService.IsTransitionAllowed(from, to)` to validate a transition before applying it.

---

## Business Rules: Failure Thresholds

When a campaign completes all chunk jobs, the `CampaignCompletionService` evaluates the final status based on the failure rate:

```
failure_rate = (failure_count / total_recipients) * 100
```

| Failure Rate | Final Status | Description |
|---|---|---|
| < 2% | `Completed` | All sends effectively succeeded. |
| ≥ 2% and < 10% | `PartialFailure` | Notable failure rate; results valid but should be reviewed. |
| ≥ 10% | `ManualReview` | High failure rate; campaign is flagged for Admin intervention. |

Progress counters updated after each chunk completion:
- `TotalRecipients` — total number of recipients targeted
- `ProcessedCount` — number of recipients processed so far (success + failure)
- `SuccessCount` — sends that reached the channel without error
- `FailureCount` — sends that encountered a dispatch error

---

## Status Transition Logging

Every status change is recorded in the `CampaignStatusHistory` table with:
- The campaign ID
- The previous (`FromStatus`) and new (`ToStatus`) statuses
- An optional free-text `Reason` for the transition
- A UTC `OccurredAt` timestamp

To retrieve the full history for a campaign:

```csharp
// Via ICampaignStatusService
IReadOnlyList<CampaignStatusTransitionDto> history =
    await statusService.GetHistoryAsync(campaignId, cancellationToken);
```

Each `CampaignStatusTransitionDto` contains:
```
Id           — unique transition record ID
CampaignId   — campaign this record belongs to
FromStatus   — string name of the previous status
ToStatus     — string name of the new status
Reason       — optional reason text (null if not provided)
OccurredAt   — UTC timestamp of the transition
```

---

## REST API: Real-Time Status Endpoint

### GET /api/campaigns/{id}/status

Returns a real-time status snapshot for a campaign, including progress counters and full status transition history.

**Authentication:** Requires a valid session cookie or `X-Api-Key` header.

**Response (200 OK):**

```json
{
  "campaignId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Summer Promo 2026",
  "status": "StepInProgress",
  "totalRecipients": 10000,
  "processedCount": 3500,
  "successCount": 3490,
  "failureCount": 10,
  "progressPercent": 35,
  "failureRatePercent": 0.10,
  "startedAt": "2026-03-27T09:00:00Z",
  "completedAt": null,
  "history": [
    {
      "fromStatus": "Draft",
      "toStatus": "Scheduled",
      "reason": "Scheduled by operator",
      "occurredAt": "2026-03-26T14:30:00Z"
    },
    {
      "fromStatus": "Scheduled",
      "toStatus": "Running",
      "reason": "Batch job started",
      "occurredAt": "2026-03-27T09:00:00Z"
    },
    {
      "fromStatus": "Running",
      "toStatus": "StepInProgress",
      "reason": "Step 1 dispatching",
      "occurredAt": "2026-03-27T09:00:05Z"
    }
  ]
}
```

**Response fields:**

| Field | Type | Description |
|---|---|---|
| `campaignId` | Guid | Campaign identifier |
| `name` | string | Campaign display name |
| `status` | string | Current lifecycle status name |
| `totalRecipients` | int | Total number of targeted recipients |
| `processedCount` | int | Recipients processed so far |
| `successCount` | int | Successful sends |
| `failureCount` | int | Failed sends |
| `progressPercent` | int | Completion percentage (0–100) |
| `failureRatePercent` | double | Failure percentage (0.00–100.00) |
| `startedAt` | datetime? | UTC timestamp when execution started (null if not started) |
| `completedAt` | datetime? | UTC timestamp when campaign completed (null if not finished) |
| `history` | array | Ordered list of status transitions (ascending by OccurredAt) |

**Error responses:**
- `404 Not Found` — campaign does not exist
- `401 Unauthorized` — no valid authentication

---

## Dashboard and UI

### Campaign List Dashboard (Index page)

The campaign list page (`/Campaigns`) provides:

- **Status summary tiles** — counts for each status group (Draft, Scheduled, Active, Completed, PartialFailure, ManualReview). Click a tile to filter the list by that status.
- **Active campaigns panel** — shown when no filter is active; lists all Running/StepInProgress campaigns with inline progress bars. Progress bars change colour based on the failure rate threshold (green → yellow at 2%, red at 10%).
- **Results table** — displays campaign name, status badge, inline progress bar, failure count, data source, steps, scheduled date, and creation date.

### Campaign Detail View

The campaign detail page (`/Campaigns/{id}`) provides:

- **Summary card** — name, data source, scheduled date, created by, status badge.
- **Progress card** — total recipients, processed count, success count, failure count, failure rate with threshold badges, and a colour-coded progress bar. When the campaign is active (Running/StepInProgress/WaitingNext), a spinner and label indicate live tracking with a 10-second auto-refresh.
- **Status history table** — collapsed/expanded section showing all status transitions in reverse-chronological order, with timestamps, from/to status badges, and optional reasons.
- **Steps table** — campaign steps with channel, template, delay, snapshot state, and execution time.

---

## Programmatic Usage

### Validating a Transition

```csharp
// Inject ICampaignStatusService
if (!_statusService.IsTransitionAllowed(campaign.Status, targetStatus))
    throw new ValidationException("Invalid status transition");

campaign.Status = targetStatus;
await _unitOfWork.CommitAsync();

await _statusService.LogTransitionAsync(
    campaign.Id, campaign.Status, targetStatus, "Reason text");
```

### Checking State Category

```csharp
// Is the campaign still processing?
bool isActive = _statusService.IsActive(campaign.Status);

// Is the campaign done (no more automatic transitions)?
bool isDone = _statusService.IsTerminal(campaign.Status);
```

### Reading Allowed Next Steps

```csharp
IReadOnlyList<CampaignStatus> nextOptions =
    _statusService.GetAllowedTransitions(campaign.Status);
```

---

## Migration Notes

The `CampaignStatusHistory` table is created via EF Core migration. The table stores one row per transition event and is append-only — existing rows are never updated or deleted.

The `Campaign` table stores the current `Status`, `TotalRecipients`, `ProcessedCount`, `SuccessCount`, `FailureCount`, `StartedAt`, and `CompletedAt` columns for fast real-time reads without joining the history table.
