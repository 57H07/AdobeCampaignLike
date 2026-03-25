# Multi-Step Campaign Guide

## Overview

CampaignEngine supports multi-step campaign sequences: ordered chains of sends across one or more channels, with configurable delays between each step. This allows operators to design complex nurture flows such as:

- Email welcome on Day 0
- Email reminder on Day +15
- SMS follow-up on Day +20

---

## Key Concepts

| Concept | Description |
|---|---|
| **Campaign Step** | A single send action within a campaign (one template, one channel, one optional delay, one optional step filter) |
| **StepOrder** | A 1-based integer that determines execution order. Steps are always processed in ascending StepOrder. |
| **DelayDays** | Days to wait after the previous step before executing this step. `0` means immediate (same day as previous step or campaign start). |
| **Step Filter** | An optional additional filter applied only to this step's audience, AND-combined with the campaign's base filter. |
| **Base Campaign Filter** | The filter defined at campaign level. Applied to all steps. |

---

## Business Rules

1. **Minimum one step** — A campaign must contain at least one step.
2. **Maximum ten steps** — A campaign may have at most 10 steps.
3. **Step orders must be unique** — No two steps may share the same `StepOrder` value.
4. **Step orders must be positive** — `StepOrder` must be 1 or greater (1-based).
5. **DelayDays must be non-negative** — Negative delays are rejected. `0` means immediate.
6. **Step filters are optional** — If omitted (`null`), no additional filtering is applied for that step.
7. **Only Published templates** — Each step must reference a template with `Published` status.

---

## Delay Calculation

Step execution dates are calculated sequentially from the campaign start date:

```
Step 1 scheduled at = campaignStart + Step1.DelayDays
Step 2 scheduled at = Step1.ScheduledAt + Step2.DelayDays
Step N scheduled at = Step(N-1).ScheduledAt + StepN.DelayDays
```

**Example:**

| Step | Channel | DelayDays | Scheduled At (campaign start = June 1) |
|------|---------|-----------|----------------------------------------|
| 1 | Email | 0 | June 1 |
| 2 | Email | 15 | June 16 |
| 3 | SMS | 20 | July 6 (June 16 + 20) |

> Note: `DelayDays` for step N is always relative to the **previous step's scheduled date**, not the campaign start date. Delays accumulate.

---

## Step-Specific Filters

Each step can define its own **step filter**, which narrows the audience for that specific step only. Step filters are AND-combined with the campaign's base filter.

**Use case:** Send a reminder email only to recipients who did not open the first email (non-respondents).

**Filter evaluation for step N:**
```
Effective audience = base campaign filter AND step N filter
```

Step filters use the same AST format as campaign base filters. See [filter-expression-syntax.md](filter-expression-syntax.md) for the full syntax reference.

---

## Creating a Multi-Step Campaign — Wizard UI

Navigate to **Campaigns > New Campaign**.

### Step 2 — Template & Steps

Use the **Add Step** button to add steps one by one. For each step:

1. Select a **Published template** (use the channel filter to narrow by Email, Letter, or SMS).
2. Set the **Delay** (days from the previous step, or from campaign start for step 1). Enter `0` for immediate.
3. Optionally click **Add Filter** to add a step-specific filter. This filter is AND-combined with the base campaign filter.

The wizard displays a **visual timeline** showing each step connected by dashed lines. The delay badge between steps shows the number of days between consecutive steps.

**Constraints enforced by the UI:**
- A maximum of 10 steps can be added. The Add Step button is disabled once the limit is reached.
- Steps are automatically renumbered when one is removed.

### Step-Specific Filter Builder

Click **Add Filter** on any step to open the step filter panel. The panel works identically to the campaign-level filter builder:

1. Click **Add Condition** to add a filter row.
2. Select a **field** from the data source schema.
3. Choose an **operator** (equals, not equals, greater than, contains, etc.).
4. Enter a **value**.

Multiple conditions are AND-combined. Click **Clear** to remove all conditions for that step.

> The step filter panel requires a data source to be selected first (Step 3 of the wizard). If no data source is selected, the step filter cannot be configured.

---

## Creating a Multi-Step Campaign — REST API

### POST /api/campaigns

Include multiple entries in the `steps` array, each with a unique `stepOrder`.

**Request body example — three-step sequence:**

```json
{
  "name": "Spring 2026 Nurture Sequence",
  "dataSourceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "filterExpression": "[{\"type\":\"leaf\",\"fieldName\":\"Status\",\"operator\":1,\"value\":\"active\"}]",
  "scheduledAt": "2026-04-01T09:00:00Z",
  "steps": [
    {
      "stepOrder": 1,
      "channel": 1,
      "templateId": "a1b2c3d4-0000-0000-0000-000000000001",
      "delayDays": 0,
      "stepFilter": null
    },
    {
      "stepOrder": 2,
      "channel": 1,
      "templateId": "a1b2c3d4-0000-0000-0000-000000000002",
      "delayDays": 15,
      "stepFilter": "[{\"type\":\"leaf\",\"fieldName\":\"Opened\",\"operator\":2,\"value\":true}]"
    },
    {
      "stepOrder": 3,
      "channel": 3,
      "templateId": "a1b2c3d4-0000-0000-0000-000000000003",
      "delayDays": 20,
      "stepFilter": null
    }
  ]
}
```

In this example:
- Step 1 sends an Email to all active recipients on April 1.
- Step 2 sends an Email reminder on April 16 (+15 days) to recipients who did NOT open the first email (`Opened != true`).
- Step 3 sends an SMS on May 6 (+20 days from Step 2) to all active recipients.

**Channel enum values:** `Email = 1`, `Letter = 2`, `Sms = 3`

**Filter operator values:** `Equals = 1`, `NotEquals = 2`, `GreaterThan = 3`, `LessThan = 4`, `GreaterThanOrEquals = 5`, `LessThanOrEquals = 6`, `Like = 7`, `In = 8`, `IsNull = 9`, `IsNotNull = 10`

---

## Viewing Campaign Steps

After creating a campaign, navigate to **Campaigns > [Campaign Name]** to see:

- The **Campaign Steps** table showing each step with its channel, template, delay, snapshot status, and execution time.
- Snapshot badges (**Frozen**) appear once the campaign is scheduled and template snapshots are taken (see the template snapshots panel).

---

## Step Execution at Runtime

When a campaign runs:

1. Each step is executed in `StepOrder` order.
2. The scheduler calculates the execution date for each step using the delay algorithm described above.
3. The base campaign filter is combined with the step-specific filter (if any) to build the effective audience for that step.
4. A template snapshot is used for rendering (frozen at scheduling time) to prevent template changes from affecting in-flight campaigns.

---

## Frequently Asked Questions

**Can two steps use different channels?**
Yes. Each step independently selects a channel and template. A campaign can mix Email, Letter, and SMS steps.

**Can two steps use the same template?**
Yes. Different steps may reference the same template ID.

**Can a step have a delay of 0?**
Yes. `DelayDays = 0` means the step is scheduled on the same date as the previous step (or campaign start for step 1). If multiple consecutive steps have `DelayDays = 0`, they share the same scheduled date.

**Are step filters evaluated against the original data source or the results of the previous step?**
Step filters are evaluated against the full data source, using the AND-combination of the campaign base filter and the step-specific filter. They do not reduce from the previous step's result set; they independently query the data source.

**What happens if a step filter excludes all recipients?**
The step is executed with zero recipients. It completes successfully with `ProcessedCount = 0`. Subsequent steps proceed on their scheduled dates.

**Can I add steps to an existing campaign?**
No. Steps are defined at campaign creation time. To change the step configuration, create a new campaign.

---

## Related Documentation

- [Campaign Creation Guide](campaign-creation-guide.md) — Full wizard walkthrough and REST API reference.
- [Filter Expression Syntax](filter-expression-syntax.md) — AST format for base and step filters.
- [Template Lifecycle Guide](template-lifecycle-guide.md) — How templates move from Draft to Published.
