# Template Snapshot Guide

## Overview

A **template snapshot** is an immutable copy of a template's fully resolved HTML body that is
frozen at the moment a campaign is scheduled. Snapshots guarantee that template edits made
after scheduling do not affect the content delivered by a running campaign.

---

## Why Snapshots Exist

Templates are living documents: operators may update text, fix mistakes, or redesign layout
at any time. Without snapshots, a template edit would silently change the content of every
campaign that references it — including campaigns already in progress.

Snapshots solve this by capturing the exact HTML at scheduling time and storing it
independently of the source template. Even if the template is edited or deleted, the
scheduled campaign continues to use its frozen content.

---

## How Snapshots Work

### Trigger: Campaign Scheduling

When an operator schedules a campaign (either via the UI wizard or
`POST /api/campaigns/{id}/schedule`), the system:

1. Loads every step's template.
2. Calls the sub-template resolver to produce the **fully resolved HTML** (all `{{> name}}`
   references substituted recursively).
3. Persists one `TemplateSnapshot` record per unique template referenced by the campaign's steps.
4. Writes the snapshot's `Id` into each `CampaignStep.TemplateSnapshotId`.
5. Transitions the campaign status to `Scheduled`.

Steps that share the same template re-use a single snapshot — they all point to the same
`TemplateSnapshotId`.

### Immutability Guarantee

After creation, a snapshot record is **never modified**. The database stores:

- `OriginalTemplateId` — the source template's GUID (preserved even if the template is deleted)
- `TemplateVersion` — the version number of the template at freeze time
- `ResolvedHtmlBody` — the fully resolved HTML (sub-templates already inlined)
- `CreatedAt` — the UTC timestamp of freeze

No code path modifies these fields after the initial `INSERT`.

---

## Sub-Template Resolution

The snapshot body is produced by resolving all `{{> name}}` references recursively (up to
5 levels of nesting). This means:

- If the template `welcome_email` embeds `{{> acme_header}}` and `{{> acme_footer}}`, the
  snapshot's `ResolvedHtmlBody` contains the fully inlined HTML of both sub-templates.
- Changes to `acme_header` after scheduling do **not** affect the snapshot.
- The snapshot is always a single, self-contained HTML string.

For details on sub-template syntax and nesting rules, see
[sub-template-composition-guide.md](./sub-template-composition-guide.md).

---

## Viewing Snapshots

### Campaign Detail Page (UI)

Open a scheduled campaign's detail page. The **Template Snapshots** card lists each
unique snapshot with:

| Column | Description |
|--------|-------------|
| Template Name | Name of the original template at freeze time |
| Channel | Email, SMS, or Letter |
| Version | Template version number captured at scheduling |
| Frozen At | UTC timestamp of snapshot creation |

Each campaign step in the steps table shows a green **Frozen** badge when a snapshot is
attached, or a grey **Not scheduled** badge for Draft campaigns.

### REST API

```
GET /api/campaigns/{id}/snapshot
```

**Authentication:** Any authenticated user (Viewer, Operator, or Admin).

**Response:** `200 OK` with an array of snapshot DTOs.

**Example response:**

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "originalTemplateId": "9c7b3d12-ae12-4f9a-b2c1-1234567890ab",
    "templateVersion": 3,
    "name": "Spring 2026 Welcome Email",
    "channel": "Email",
    "resolvedHtmlBody": "<!DOCTYPE html>...<full resolved HTML>...",
    "createdAt": "2026-03-25T14:32:00Z"
  }
]
```

Returns an empty array `[]` if the campaign has not been scheduled yet (status `Draft`).

Returns `404 Not Found` if the campaign ID does not exist.

---

## Database Schema

Snapshots are stored in the `TemplateSnapshots` table:

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `uniqueidentifier` | Snapshot primary key |
| `OriginalTemplateId` | `uniqueidentifier` | Source template GUID |
| `TemplateVersion` | `int` | Template version at freeze time |
| `Name` | `nvarchar(300)` | Template name at freeze time |
| `Channel` | `int` | Channel enum (0=Email, 1=Sms, 2=Letter) |
| `ResolvedHtmlBody` | `nvarchar(max)` | Fully resolved HTML |
| `CreatedAt` | `datetime2` | UTC creation timestamp |
| `CreatedBy` | `nvarchar(256)` | Operator username |

Campaign steps reference snapshots via `CampaignSteps.TemplateSnapshotId` (nullable FK).

---

## Business Rules

| Rule | Description |
|------|-------------|
| BR-1 | Snapshot created when campaign transitions to `Scheduled` status |
| BR-2 | One snapshot per unique template (steps sharing the same template share one snapshot) |
| BR-3 | Snapshots are immutable after creation — no update operations exist |
| BR-4 | Snapshot persists even if the source template is later deleted or archived |
| BR-5 | `ResolvedHtmlBody` contains fully resolved sub-templates (no `{{> name}}` references remain) |
| BR-6 | Snapshot creation is atomic with the campaign status change |

---

## Audit and Reproducibility

Because snapshots are stored in the database indefinitely:

- Operators can always inspect exactly what content was sent for any historical campaign.
- The `GET /api/campaigns/{id}/snapshot` endpoint provides a machine-readable audit record.
- Deleting a source template does **not** remove its associated snapshots.

---

## Example Workflow

1. Operator creates a campaign referencing template `Spring Newsletter v3`.
2. Operator schedules the campaign for `2026-04-01 08:00 UTC`.
3. At scheduling time the system freezes `Spring Newsletter v3` (including any embedded
   sub-templates such as `acme_header` and `acme_footer`) into a `TemplateSnapshot`.
4. Another operator later updates `Spring Newsletter v3` to change the offer code.
5. The scheduled campaign is **unaffected** — it will deliver the content frozen at step 3.
6. A viewer can audit the frozen content at any time via:
   ```
   GET /api/campaigns/{campaignId}/snapshot
   ```

---

## Related Documentation

- [campaign-creation-guide.md](./campaign-creation-guide.md) — Campaign wizard and scheduling
- [sub-template-composition-guide.md](./sub-template-composition-guide.md) — Sub-template syntax and resolution
- [template-lifecycle-guide.md](./template-lifecycle-guide.md) — Template status workflow (Draft, Published, Archived)
