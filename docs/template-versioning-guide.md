# Template Version Management Guide

## Overview

CampaignEngine automatically tracks every change to a template as a versioned snapshot. This allows designers to:

- Review what changed between any two versions
- Revert to a previous version if needed
- Ensure audit compliance (history is never deleted)

---

## How Versioning Works

### Version numbering

- Every template starts at **version 1** when first created.
- Every time a template is saved (PUT /api/templates/{id}), the version number **auto-increments by 1**.
- The current version number is shown on the Template Detail page and in all API responses.

### Version snapshots

Before each update is applied, the **current state** is saved to the `TemplateHistory` table. This snapshot captures:
- The version number at time of change
- The full HTML body
- The template name
- The channel type
- The user who made the change (if authenticated)

The `TemplateHistory` table **never has records deleted** — it is an append-only audit log.

### Business rules

| Rule | Description |
|------|-------------|
| Version starts at 1 | All new templates begin at version 1 |
| Increment on every save | Every PUT /api/templates/{id} creates a new version |
| History never deleted | Audit requirement — history is permanent |
| Revert creates new version | Reverting to v3 while at v7 creates v8 with v3 content |
| Campaign snapshots | Campaigns freeze template content at scheduling time (US-025) |

---

## API Reference

### GET /api/templates/{id}/history

Returns all version history entries for the specified template, ordered by version descending.

**Authorization:** Any authenticated user

**Response:** Array of `TemplateHistoryDto`

```json
[
  {
    "id": "...",
    "templateId": "...",
    "version": 3,
    "name": "Welcome Email",
    "channel": "Email",
    "htmlBody": "<p>Previous content</p>",
    "changedBy": "marie@example.com",
    "createdAt": "2026-03-20T14:30:00Z"
  },
  {
    "id": "...",
    "templateId": "...",
    "version": 2,
    "name": "Welcome Email",
    "channel": "Email",
    "htmlBody": "<p>Even older content</p>",
    "changedBy": "marie@example.com",
    "createdAt": "2026-03-19T10:00:00Z"
  }
]
```

> **Note:** The current live version is NOT in the history list — it is available via GET /api/templates/{id}. History contains only the saved-before-update snapshots.

---

### GET /api/templates/{id}/history/diff?fromVersion=N&toVersion=M

Returns a diff comparing two versions of a template.

**Authorization:** Any authenticated user

**Query parameters:**
- `fromVersion` (required, int >= 1): The older (base) version
- `toVersion` (optional, int): The newer version. If omitted, compares against the current live version.

**Response:** `TemplateDiffDto`

```json
{
  "templateId": "...",
  "fromVersion": 2,
  "toVersion": 4,
  "fromHtmlBody": "<p>old body</p>",
  "toHtmlBody": "<p>new body</p>",
  "fromName": "Old Name",
  "toName": "New Name",
  "nameChanged": true,
  "htmlBodyChanged": true
}
```

**Error responses:**
- `404 Not Found` if `fromVersion` or `toVersion` does not exist in history
- `400 Bad Request` if `fromVersion` is less than 1

---

### POST /api/templates/{id}/revert/{version}

Reverts a template to a previous version by creating a new version with the historic content.

**Authorization:** Designer or Admin role required

**Path parameters:**
- `id` (GUID): Template identifier
- `version` (int >= 1): Version number to revert to

**Behaviour:**
1. The current template state is saved to `TemplateHistory` as a snapshot
2. The template's content is replaced with the content from the specified historic version
3. The version counter increments by 1

**Example:** Template is at v7. Reverting to v3:
- v7 content is saved to history as a snapshot
- Template now has v3 content
- Template version becomes v8

**Response:** Updated `TemplateDto` with the new version number

**Error responses:**
- `404 Not Found` if the template or target version does not exist
- `400 Bad Request` if version is less than 1
- `403 Forbidden` if the user does not have Designer or Admin role

---

## Frontend: Version History Page

The version history page is accessible from the Template Detail page via the **Version History** button, or directly at:

```
/Templates/History/{id}
```

### Features

**Version list (left panel):**
- Shows all historic versions plus the current live version
- Each entry shows: version number, template name, changed-by user, timestamp
- Clicking **Diff** on any historic version shows a side-by-side comparison with the current version
- For Designer/Admin users: **Revert** button on each historic version

**Diff panel (right panel):**
- Side-by-side comparison of the two selected versions
- Name changes highlighted when the template name was modified
- HTML body changes shown in a code view with before/after panels
- If no differences exist, a "No differences" message is shown

**Revert confirmation dialog (TASK-008-07):**
- Appears when clicking a **Revert** button
- Shows the version being reverted to and a warning about the implications
- Requires explicit confirmation before executing
- On success, redirects back to the history page with a status message

---

## Viewing Version History in the UI

1. Navigate to **Templates** → select a template → click **Version History**
2. The history list on the left shows all saved versions
3. Click **Diff** next to any version to see a side-by-side comparison
4. To compare two specific historic versions, use the API directly:
   `GET /api/templates/{id}/history/diff?fromVersion=2&toVersion=5`

---

## Reverting to a Previous Version

### Via the UI

1. Navigate to the template's Version History page
2. Find the version you want to revert to in the list
3. Click **Revert** and confirm in the dialog
4. The template will be updated to reflect the historic content, and a new version will be created

### Via the API

```http
POST /api/templates/{id}/revert/{version}
Authorization: Bearer <token>
```

---

## FAQ

**Q: Can I delete version history?**
A: No. Version history is permanent for audit compliance (business rule BR-2). The `TemplateHistory` table is append-only.

**Q: Does reverting overwrite history?**
A: No. Reverting always creates a new version. Your current content is saved to history before the revert is applied.

**Q: What happens to running campaigns when I revert a template?**
A: Running campaigns use frozen template snapshots (created at campaign scheduling time). They are not affected by template reverts. Only future campaigns using the template will see the reverted content.

**Q: What is the difference between "Template History" and "Template Snapshot"?**
A: `TemplateHistory` is the designer's version audit log — it records every edit. `TemplateSnapshot` (US-025) is a campaign-level freeze of the full template content (including resolved sub-templates) created when a campaign is scheduled.

**Q: Who can revert a template?**
A: Only users with the **Designer** or **Admin** role can revert templates. Viewing version history is available to all authenticated users.
