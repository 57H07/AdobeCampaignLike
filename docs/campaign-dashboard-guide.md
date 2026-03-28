# Campaign Progress Dashboard Guide

This document describes the campaign progress dashboard feature (US-036) in CampaignEngine. The dashboard gives Campaign Operators a real-time aggregate view of all active campaigns.

## Overview

The dashboard page is available at `/Campaigns/Dashboard` and provides:

- An aggregate summary (active campaign count, total recipients, successful sends, failures)
- Per-campaign metric cards with progress bars and estimated completion time
- Multi-step campaign timeline visualization
- Automatic refresh every 10 seconds

## Accessing the Dashboard

Navigate to **Campaigns → Live Dashboard** in the top navigation, or go directly to `/Campaigns/Dashboard`.

Any authenticated user (Operator, Admin, or API consumer) can view the dashboard.

## What Is Shown

### Summary Bar

The top of the dashboard shows four aggregate counters:

| Counter | Description |
|---------|-------------|
| Active Campaigns | Number of campaigns currently Running or StepInProgress |
| Total Recipients | Sum of all targeted recipients across active campaigns |
| Sent Successfully | Total successful sends across all active campaigns |
| Failed Sends | Total failed sends across all active campaigns |

### Campaign Cards

Each active campaign is displayed as a card containing:

- **Campaign name** (links to campaign detail page)
- **Status badge** (Running, StepInProgress, etc.)
- **Operator** (username who created the campaign)
- **Metric row**: Total recipients / Processed / Sent / Failed
- **Progress bar**: Shows percentage processed, color-coded by failure rate:
  - Green — failure rate < 2%
  - Yellow/Warning — failure rate 2–10%
  - Red/Danger — failure rate ≥ 10%
- **Estimated completion time** (ETA): when available, shows the estimated UTC time for processing to complete, based on the current send rate
- **Step timeline**: for multi-step campaigns, shows each step with its channel icon, status indicator, and scheduled/executed timestamps

### Step Timeline Status Icons

| Icon | Meaning |
|------|---------|
| Green check-circle | Step completed (ExecutedAt is set) |
| Yellow play-circle | Step active (ScheduledAt is in the past, not yet executed) |
| Blue hourglass | Step waiting (ScheduledAt is in the future) |
| Grey circle | Step pending (not yet scheduled) |

Between steps, an arrow connector shows the delay in days (e.g., `+2d`) if a step has a `DelayDays` value.

## Filters

Use the filter bar at the top of the dashboard to narrow results:

| Filter | Description | Default |
|--------|-------------|---------|
| Status | Campaign status or comma-separated list (e.g. `Running`, `Running,StepInProgress,WaitingNext`) | Running and StepInProgress |
| Started From | Only show campaigns started on or after this UTC date | None (no lower bound) |
| Started To | Only show campaigns started on or before this UTC date | None (no upper bound) |
| Operator | Filter by `CreatedBy` username | None (all operators) |

Click the funnel icon to apply filters. Click the X icon to clear all filters and return to the default view.

## Auto-Refresh

The dashboard automatically polls `GET /api/campaigns/dashboard` every **10 seconds**.

- A countdown badge in the bottom-right corner shows time until the next refresh.
- Click **Refresh Now** to trigger an immediate refresh without waiting.
- The "Last refreshed" timestamp shows exactly when the data was last fetched.

No full page reload occurs — the campaign cards and summary bar are updated in-place via JavaScript.

## REST API Endpoint

The dashboard data is exposed via a REST API endpoint for external integration:

```
GET /api/campaigns/dashboard
```

**Authentication**: `X-Api-Key` header or authenticated session cookie.

**Query parameters** (all optional):

| Parameter | Type | Description |
|-----------|------|-------------|
| `status` | string | Comma-separated status names or integers. Default: `Running,StepInProgress` |
| `startedFrom` | ISO-8601 datetime | UTC lower bound for `StartedAt` |
| `startedTo` | ISO-8601 datetime | UTC upper bound for `StartedAt` |
| `createdBy` | string | Filter by operator username |

**Example request:**

```http
GET /api/campaigns/dashboard?status=Running,StepInProgress&createdBy=thomas
X-Api-Key: <your-api-key>
Accept: application/json
```

**Example response:**

```json
{
  "computedAtUtc": "2026-03-28T14:30:00Z",
  "activeCampaignCount": 2,
  "campaigns": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "Spring Promotion",
      "status": "Running",
      "createdBy": "thomas",
      "totalRecipients": 10000,
      "processedCount": 7500,
      "successCount": 7400,
      "failureCount": 100,
      "progressPercent": 75,
      "failureRatePercent": 1.0,
      "estimatedCompletionUtc": "2026-03-28T14:35:00Z",
      "startedAt": "2026-03-28T14:00:00Z",
      "scheduledAt": "2026-03-28T14:00:00Z",
      "steps": [
        {
          "id": "...",
          "stepOrder": 1,
          "channel": "Email",
          "delayDays": 0,
          "scheduledAt": "2026-03-28T14:00:00Z",
          "executedAt": "2026-03-28T14:01:00Z",
          "stepStatus": "Completed"
        },
        {
          "id": "...",
          "stepOrder": 2,
          "channel": "Sms",
          "delayDays": 3,
          "scheduledAt": "2026-03-31T14:00:00Z",
          "executedAt": null,
          "stepStatus": "Waiting"
        }
      ]
    }
  ]
}
```

## Estimated Completion Time Calculation

The ETA is computed using a simple linear extrapolation based on the current processing rate:

```
rate_per_second = processedCount / elapsed_seconds_since_start
remaining       = totalRecipients - processedCount
eta             = now + remaining / rate_per_second
```

ETA is `null` when:
- The campaign has not started (`StartedAt` is null)
- No recipients have been processed yet (`processedCount == 0`)
- All recipients have been processed (`processedCount >= totalRecipients`)

## Business Rules

1. By default, the dashboard shows only campaigns in **Running** or **StepInProgress** status.
2. Metrics are read directly from the campaign entity counters (updated after each chunk completes).
3. Campaigns are ordered by `StartedAt` descending (most recently started first).
4. The dashboard does not show terminal campaigns (Completed, PartialFailure, ManualReview) unless explicitly requested via the status filter.

## Integration Use Cases

- **Monitoring script**: Poll `/api/campaigns/dashboard` every minute to alert on high failure rates.
- **CI/CD gate**: Check `failureRatePercent` before marking a campaign send as successful.
- **Operations screen**: Embed the dashboard URL in an ops monitoring portal (authenticated via API key).
