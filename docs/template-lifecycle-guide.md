# Template Lifecycle Guide

This document describes the template lifecycle workflow in CampaignEngine, covering status transitions, business rules, and API usage.

## Overview

Templates follow a one-way lifecycle: **Draft → Published → Archived**.

```
  [New Template]
        |
        v
     DRAFT  ────────────────────────────────────> ARCHIVED
        |                                              ^
        | (Publish: complete manifest required)        |
        v                                              |
   PUBLISHED ─────────────────────────────────────────┘
                (Archive: available to Designer/Admin)
```

**Key rules:**
- New templates always start as **Draft**
- Only **Published** templates can be used in campaign creation
- **Archived** templates are visible for audit purposes but cannot be used in new campaigns
- Archived templates **cannot** transition back to Published

---

## Status Descriptions

| Status    | Description |
|-----------|-------------|
| `Draft`   | Template is under development. Can be freely edited. Not available for campaign creation. |
| `Published` | Template is validated and ready for production use. Available for campaign creation. Can be edited (creates a new version) or archived. |
| `Archived` | Template is retired. Visible for audit purposes only. Cannot be used in campaigns. Cannot be published again. |

---

## Transition Rules

### Draft → Published (Publish)

**Endpoint:** `POST /api/templates/{id}/publish`

**Required conditions:**
1. Template must currently be in `Draft` status
2. All placeholders used in the HTML body must be declared in the placeholder manifest

**Validation errors:**
- `"Template 'X' cannot be published: current status is 'Published'. Only Draft templates can be published."` — template is already published
- `"Template 'X' cannot be published: placeholder manifest is incomplete. Undeclared placeholders: 'key1', 'key2'..."` — manifest validation failed

### Draft/Published → Archived (Archive)

**Endpoint:** `POST /api/templates/{id}/archive`

**Required conditions:**
1. Template must not already be `Archived`

**Validation errors:**
- `"Template 'X' is already Archived. Archived templates cannot change status."` — template is already archived

---

## API Reference

### Publish a Template

```http
POST /api/templates/{id}/publish
Authorization: Bearer {token}

# Response (200 OK)
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Welcome Email",
  "channel": "Email",
  "status": "Published",
  "version": 1,
  ...
}

# Error Response (400 Bad Request) — incomplete manifest
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Validation error",
  "status": 400,
  "detail": "Template 'Welcome Email' cannot be published: placeholder manifest is incomplete. Undeclared placeholders: 'name', 'ref'."
}
```

### Archive a Template

```http
POST /api/templates/{id}/archive
Authorization: Bearer {token}

# Response (200 OK)
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Old Promo Email",
  "channel": "Email",
  "status": "Archived",
  "version": 3,
  ...
}
```

---

## Business Rules

1. **New templates start as Draft** — status is set automatically at creation
2. **Draft → Published requires a complete manifest** — all `{{ placeholder }}` keys in the HTML body must be declared in the placeholder manifest before publishing
3. **Published templates can be archived** — use `POST /api/templates/{id}/archive`
4. **Draft templates can be archived** — useful to discard templates that will never be published
5. **Archived templates cannot transition back to Published** — this is a permanent, irreversible state
6. **Only Admin can force-archive templates in active campaigns** — this prevents data integrity issues

---

## Frontend Workflow

### Template Detail Page

The template detail page (`/Templates/Detail/{id}`) shows context-sensitive action buttons:

| Current Status | Available Actions |
|----------------|------------------|
| Draft          | Edit, Publish, Archive |
| Published      | Edit, Archive |
| Archived       | (read-only — no actions) |

### Template List Page

The template list (`/Templates`) shows inline action buttons:

- **Publish** button appears for Draft templates only
- **Archive** button appears for Draft and Published templates
- **Edit** button is hidden for Archived templates

---

## Audit Logging

All status transitions are logged with the following information:

- Template ID and Name
- Previous status and new status
- UTC timestamp (via `UpdatedAt` field on the entity)
- Logged via the structured logging framework (Serilog)

---

## Placeholder Manifest Validation

Before a template can be published, the placeholder manifest must be **complete**. This means every placeholder key found in the template HTML body must have a corresponding entry in the manifest.

**Example:** Template HTML:
```html
<p>Dear {{ customer_name }},</p>
<p>Your order {{ order_ref }} has been confirmed.</p>
```

**Required manifest entries:**

| Key            | Type   | Source      |
|----------------|--------|-------------|
| customer_name  | Scalar | DataSource  |
| order_ref      | Scalar | DataSource  |

**Validation endpoint:** `GET /api/templates/{id}/placeholders/validate`

This returns whether the manifest is complete and lists any undeclared placeholders.

---

## Campaign Integration

Only **Published** templates appear in the campaign creation template selector. This ensures:

- Operators cannot accidentally select incomplete templates
- Template content is validated before any campaign runs
- Draft templates are protected from unintended production use

When a campaign is scheduled, the Published template content is **frozen** into a snapshot (see [Template Snapshot documentation](database-schema-and-migrations.md)) — subsequent template archives or edits do not affect already-scheduled campaigns.
