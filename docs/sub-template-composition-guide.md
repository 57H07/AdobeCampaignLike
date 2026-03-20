# Sub-Template Composition Guide

## Overview

Sub-templates are reusable HTML blocks that can be embedded in parent templates.
They are useful for maintaining brand consistency by centralising repeated elements
such as headers, footers, and signature blocks.

When a parent template is rendered, all sub-template references are resolved
**recursively** before the Scriban engine processes the remaining placeholders.
This means changes to a sub-template automatically propagate to every parent
template that references it (except frozen campaign snapshots).

---

## Creating a Sub-Template

1. Navigate to **Templates** and click **New Template**.
2. Fill in the name, channel, description, and HTML body.
3. Enable the **"Sub-template block"** toggle before saving.

A sub-template is a regular `Template` entity with `IsSubTemplate = true`.
Sub-templates are visible in the template list with a **"Sub"** badge.

> Sub-templates follow the same naming rules as regular templates:
> the name must be unique within the channel.

---

## Embedding a Sub-Template

Use the `{{> name}}` syntax anywhere in a parent template's HTML body:

```html
{{> company_header}}

<p>Dear {{ recipient_name }},</p>
<p>{{ message_body }}</p>

{{> company_footer}}
```

Where `company_header` and `company_footer` are the **names** of existing
sub-templates (case-insensitive, whitespace-trimmed).

### Multiple occurrences

The same sub-template can appear multiple times:

```html
{{> section_divider}}
<p>Section 1</p>
{{> section_divider}}
<p>Section 2</p>
```

Both occurrences are replaced with the sub-template's resolved body.

---

## Recursive Nesting

Sub-templates can reference other sub-templates. The resolution engine resolves
the full tree before rendering:

```
parent_template
  -> company_footer (sub-template)
       -> email_signature (sub-template)
```

**Maximum nesting depth: 5 levels.** If the depth exceeds 5, a validation
error is thrown and the template cannot be rendered.

---

## Sub-Template Selector (Template Editor)

When editing a template, the right-hand panel shows all available sub-templates.

- Click a `{{> name}}` snippet to insert it at the current cursor position.
- Sub-templates already referenced in the HTML body are highlighted with a
  **"Used"** badge.
- The "Referenced Sub-templates" panel below the editor lists all currently
  embedded sub-templates.

---

## Circular Reference Detection

Circular references are **detected and rejected** at render time and during
explicit validation.

### What is a circular reference?

A circular reference occurs when a chain of sub-templates eventually references
itself:

```
block_a -> block_b -> block_a   (cycle)
```

Any such cycle causes a `ValidationException` with a message identifying the
full reference chain.

### Validation API

You can proactively validate a template for circular references without rendering:

```
GET /api/templates/{id}/subtemplates/validate
```

Response:

```json
{
  "templateId": "...",
  "isValid": true,
  "message": "No circular sub-template references detected."
}
```

If a cycle is found:

```json
{
  "templateId": "...",
  "isValid": false,
  "message": "Circular sub-template reference detected: block_a -> block_b -> block_a -> block_a (guid)"
}
```

---

## API Reference

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/templates/subtemplates` | List all sub-templates |
| GET | `/api/templates/{id}/subtemplates/references` | Extract direct {{> name}} references |
| POST | `/api/templates/{id}/subtemplates/resolve` | Resolve all sub-templates recursively |
| GET | `/api/templates/{id}/subtemplates/validate` | Validate for circular references |

### GET /api/templates/subtemplates

Returns all templates with `IsSubTemplate = true`.

```json
[
  {
    "id": "...",
    "name": "company_header",
    "channel": "Email",
    "status": "Published",
    "isSubTemplate": true,
    "description": "Standard ACME Corp header block"
  }
]
```

### GET /api/templates/{id}/subtemplates/references

```json
{
  "templateId": "...",
  "references": ["company_header", "company_footer"]
}
```

### POST /api/templates/{id}/subtemplates/resolve

Returns the fully resolved HTML body (sub-templates replaced) without persisting anything.
Useful for live preview.

```json
{
  "templateId": "...",
  "resolvedHtmlBody": "<header>...</header><p>Content</p><footer>...</footer>",
  "isFullyResolved": true
}
```

---

## Business Rules

| Rule | Description |
|------|-------------|
| BR-1 | Sub-template syntax: `{{> name}}` (Handlebars partial syntax) |
| BR-2 | Maximum nesting depth: 5 levels |
| BR-3 | Circular references throw `ValidationException` |
| BR-4 | Only templates with `IsSubTemplate = true` are resolved by name |
| BR-5 | Sub-templates inherit the channel context of the parent template |
| BR-6 | Template name lookup is case-insensitive |

---

## Campaign Snapshots and Sub-Templates

When a campaign is scheduled, its template content is **frozen** into a
`TemplateSnapshot`. The snapshot includes the **fully resolved HTML body**
(all sub-templates already substituted).

This means:
- Changes to sub-templates do **not** affect already-scheduled campaigns.
- The snapshot preserves the exact content as it was at scheduling time.
- For live templates (not yet scheduled), changes to sub-templates propagate
  immediately to the parent's preview.

---

## Example: Email Template with Header and Footer

### Sub-template: `acme_header`

```html
<table width="100%" cellpadding="0" cellspacing="0">
  <tr>
    <td style="background:#003366; padding:20px;">
      <img src="https://acme.example.com/logo.png" alt="ACME Corp" height="40" />
    </td>
  </tr>
</table>
```

### Sub-template: `acme_footer`

```html
<table width="100%" cellpadding="0" cellspacing="0">
  <tr>
    <td style="background:#f5f5f5; padding:15px; font-size:12px; color:#666;">
      <p>ACME Corporation &bull; 123 Business St &bull; <a href="mailto:support@acme.example.com">Contact Us</a></p>
      <p>You received this email because you are a customer of ACME Corp.</p>
    </td>
  </tr>
</table>
```

### Parent template: `welcome_email`

```html
<!DOCTYPE html>
<html>
<body>

{{> acme_header}}

<table width="100%" cellpadding="0" cellspacing="0">
  <tr>
    <td style="padding:30px;">
      <h1>Welcome, {{ first_name }}!</h1>
      <p>Your account has been created successfully.</p>
      <p>Account number: <strong>{{ account_number }}</strong></p>
    </td>
  </tr>
</table>

{{> acme_footer}}

</body>
</html>
```

The rendered output will have the header and footer blocks injected in place of
the `{{> acme_header}}` and `{{> acme_footer}}` references, with Scriban
then substituting `{{ first_name }}` and `{{ account_number }}` from the
campaign data.
