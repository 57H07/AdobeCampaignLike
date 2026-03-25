# Campaign Creation Guide

## Overview

This guide explains how to create and configure campaigns in CampaignEngine using the wizard interface or the REST API.

A campaign is a targeted message operation that sends content to a set of recipients from a data source, using one or more steps (templates), optionally filtered and scheduled.

---

## Key Concepts

| Concept | Description |
|---|---|
| **Campaign** | A named, orchestrated send operation targeting recipients from a data source |
| **Campaign Step** | A single send action within a campaign (one template, one channel, one optional delay) |
| **Data Source** | The database or repository that provides the recipient list |
| **Filter Expression** | An AST-based filter that selects a subset of data source records |
| **Free Field** | A placeholder value provided by the operator at campaign creation time (same value for all recipients) |
| **Schedule** | When to start the campaign: immediate (manual trigger) or a future UTC date/time |

---

## Business Rules

1. **Campaign name must be unique** — Two campaigns cannot share the same name (enforced at creation).
2. **Only Published templates** — Campaign steps must reference templates in `Published` status. Draft and Archived templates are rejected.
3. **Free fields are mandatory** — All `FreeField` placeholders declared in any selected template's manifest must be provided at campaign creation.
4. **Schedule must be at least 5 minutes in the future** — If a scheduled date is provided, it must be at least 5 minutes ahead of the current UTC time.
5. **At least one step required** — A campaign must have at least one step.

---

## Campaign Wizard (UI)

Navigate to **Campaigns > New Campaign** to open the 5-step wizard.

### Step 1 — Campaign Name

Enter a unique, descriptive name for the campaign (max 300 characters).

**Example:** `Spring 2026 Email Offer`

### Step 2 — Template & Steps

Add one or more steps. Each step requires:

- **Template** — Select a Published template. Use the channel filter to narrow down templates by Email, Letter, or SMS.
- **Delay** — Days to wait after the previous step (0 = same day / immediate). For the first step, this is the delay from campaign start.

**Multi-step example:**
- Step 1: Email Welcome (Day 0)
- Step 2: Email Reminder (Day +15)
- Step 3: SMS Final Nudge (Day +30)

### Step 3 — Data Source & Filter

Select the **data source** that provides the recipient list. Leave blank if recipients will be targeted manually.

Optionally add **filter conditions** to segment recipients. Each condition operates on a filterable field declared in the data source schema.

**Example filter:** `Status = "active" AND Age >= 18`

Click **Estimate Recipients** to get an approximate count of matching records before proceeding.

### Step 4 — Free Field Values

If any of the selected templates declare `FreeField` placeholders (values not sourced from the data source), provide those values here.

Free field values are the same for **all recipients** — they are not personalized.

**Example:** `offerCode = "SPRING2026"`, `discount = "20%"`

### Step 5 — Schedule

Choose when the campaign will run:

- **Immediate** — Campaign is created in `Draft` status and must be triggered manually.
- **Scheduled** — Provide a UTC date and time at least 5 minutes in the future. The campaign will transition to `Scheduled` status automatically.

Review the campaign summary and click **Create Campaign** to finalize.

---

## REST API

### POST /api/campaigns

Creates a new campaign in `Draft` status.

**Authorization:** Operator or Admin role required.

**Request body:**

```json
{
  "name": "Spring 2026 Email Offer",
  "dataSourceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "filterExpression": "[{\"type\":\"leaf\",\"fieldName\":\"Status\",\"operator\":1,\"value\":\"active\"}]",
  "freeFieldValues": "{\"offerCode\":\"SPRING2026\",\"discount\":\"20%\"}",
  "scheduledAt": "2026-04-01T09:00:00Z",
  "steps": [
    {
      "stepOrder": 1,
      "channel": 1,
      "templateId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "delayDays": 0
    },
    {
      "stepOrder": 2,
      "channel": 3,
      "templateId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
      "delayDays": 15
    }
  ]
}
```

**Channel enum values:** `Email = 1`, `Letter = 2`, `Sms = 3`

**Filter operator values:** `Equals = 1`, `NotEquals = 2`, `GreaterThan = 3`, `LessThan = 4`, `GreaterThanOrEquals = 5`, `LessThanOrEquals = 6`, `Like = 7`, `In = 8`, `IsNull = 9`, `IsNotNull = 10`

**Success response (201 Created):**

```json
{
  "id": "c4d5e6f7-...",
  "name": "Spring 2026 Email Offer",
  "status": "Draft",
  "dataSourceId": "3fa85f64-...",
  "dataSourceName": "CRM Database",
  "scheduledAt": "2026-04-01T09:00:00Z",
  "steps": [
    { "stepOrder": 1, "channel": "Email", "templateId": "a1b2c3...", "delayDays": 0 },
    { "stepOrder": 2, "channel": "Sms",   "templateId": "b2c3d4...", "delayDays": 15 }
  ],
  "createdAt": "2026-03-25T10:00:00Z"
}
```

**Error response (400 Bad Request):**

```json
{
  "errors": {
    "name": ["A campaign named 'Spring 2026 Email Offer' already exists."],
    "steps": ["Only Published templates can be used in campaigns. Non-published: My Draft Template."]
  }
}
```

---

### GET /api/campaigns

Returns a paginated list of campaigns.

**Query parameters:**

| Parameter | Type | Description |
|---|---|---|
| `status` | int (optional) | Filter by status: `Draft=1`, `Scheduled=2`, `Running=3`, `Completed=6` |
| `nameContains` | string (optional) | Substring search on campaign name |
| `dataSourceId` | GUID (optional) | Filter by data source |
| `page` | int (default 1) | Page number |
| `pageSize` | int (default 20, max 100) | Items per page |

**Example:**

```
GET /api/campaigns?status=1&page=1&pageSize=20
```

---

### GET /api/campaigns/{id}

Returns a single campaign by ID, including all steps.

---

### POST /api/campaigns/estimate-recipients

Estimates the number of recipients for a data source and filter combination **before** creating the campaign. This is a read-only operation.

**Request body:**

```json
{
  "dataSourceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "filterExpression": "[{\"type\":\"leaf\",\"fieldName\":\"Status\",\"operator\":1,\"value\":\"active\"}]"
}
```

**Response:**

```json
{
  "estimatedCount": 12450,
  "isSuccess": true,
  "errorMessage": null
}
```

---

## Filter Expression Format

Campaign filters use the same AST format as the Data Source Filter Builder. See [filter-expression-syntax.md](filter-expression-syntax.md) for the full specification.

**Quick reference:**

```json
[
  {
    "type": "leaf",
    "fieldName": "Country",
    "operator": 1,
    "value": "FR"
  },
  {
    "type": "composite",
    "logicalOperator": 2,
    "children": [
      { "type": "leaf", "fieldName": "Plan", "operator": 1, "value": "premium" },
      { "type": "leaf", "fieldName": "Plan", "operator": 1, "value": "enterprise" }
    ]
  }
]
```

This means: `Country = 'FR' AND (Plan = 'premium' OR Plan = 'enterprise')`

---

## Campaign Lifecycle

After creation, campaigns transition through these states:

```
Draft → Scheduled → Running → StepInProgress → Completed
                                              → PartialFailure (>2% failures)
                                              → ManualReview (>10% failures)
```

Draft campaigns must be manually scheduled or triggered by an Operator/Admin.

---

## Free Field Values Format

Free field values are stored as a JSON object where keys match the placeholder keys declared in the template manifest:

```json
{"offerCode": "SPRING2026", "expiryDate": "2026-04-30", "discount": "20%"}
```

All `FreeField` type placeholders in all selected templates must have a corresponding key in this object.

---

## Notes

- Campaigns are soft-deleted — deleting a campaign sets `IsDeleted = true` and preserves all send logs.
- Template snapshots are created when a campaign transitions to `Scheduled` status (see US-025).
- Recipient count estimates are approximate — the actual count is determined at execution time.
