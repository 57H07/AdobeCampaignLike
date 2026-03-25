# Template Preview Guide

## Overview

The template preview feature (US-010) allows Template Designers and Administrators to render a template with real sample data from a configured data source before publishing. Preview is strictly read-only — no messages are sent and no records are written.

## Who Can Use Preview

- **Designer** role
- **Admin** role

Operator users do not have access to the preview feature.

## How to Preview a Template

### From the Web UI

1. Navigate to **Templates** in the sidebar.
2. Open any template by clicking its name.
3. Click the **Preview** button (eye icon) in the top-right action bar.
4. In the **Preview Configuration** panel:
   - **Data Source**: Select the data source from which sample rows will be fetched.
   - **Sample Rows**: Choose how many rows to fetch (1–5). The default is 5.
   - **Row to Render**: Choose which row index (1–5) to use for rendering. The default is Row 1.
5. Click **Run Preview**.

The system will:
- Fetch up to N sample rows from the selected data source (read-only query).
- Resolve any sub-template references (`{{> name}}` syntax) in the template body.
- Render the template using the selected row's data.
- Apply channel-specific post-processing (see below).
- Display the rendered output.

### From the REST API

**Endpoint:** `POST /api/templates/{id}/preview`

**Authorization:** Designer or Admin role required.

**Request body:**
```json
{
  "dataSourceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "sampleRowCount": 5,
  "rowIndex": 0
}
```

| Field | Type | Description | Default |
|-------|------|-------------|---------|
| `dataSourceId` | `guid` | ID of the active data source to fetch sample rows from. Required. | — |
| `sampleRowCount` | `int` | Number of rows to fetch (1–5). | `5` |
| `rowIndex` | `int` | Zero-based index of the row to render (0 = first row). | `0` |

**Response (200 OK):**
```json
{
  "templateId": "...",
  "channel": "Email",
  "contentType": "text/html",
  "textContent": "<p style=\"color:#333\">Hello Alice</p>",
  "base64Content": null,
  "sampleRows": [
    { "name": "Alice", "email": "alice@example.com" }
  ],
  "rowUsed": 0,
  "totalSampleRows": 1,
  "missingPlaceholders": [],
  "isSuccess": true,
  "errorMessage": null
}
```

**Response fields:**

| Field | Description |
|-------|-------------|
| `templateId` | ID of the previewed template. |
| `channel` | Channel type (`Email`, `Letter`, `Sms`). |
| `contentType` | MIME type of the output (`text/html`, `application/pdf`, `text/plain`). |
| `textContent` | Rendered text content (Email HTML or SMS plain text). Null for PDF. |
| `base64Content` | Rendered PDF as Base64 (Letter channel only). Null for text channels. |
| `sampleRows` | The sample rows fetched from the data source. |
| `rowUsed` | Zero-based index of the row used for rendering. |
| `totalSampleRows` | Total number of rows fetched. |
| `missingPlaceholders` | Placeholder keys present in the template but absent from the sample row. |
| `isSuccess` | `true` if rendering succeeded; `false` on error. |
| `errorMessage` | Error description when `isSuccess` is `false`. |

## Channel-Specific Output

| Channel | Post-Processing | Output Format |
|---------|----------------|---------------|
| **Email** | CSS inlining (PreMailer.Net) | `text/html` — inline-styled HTML ready for email clients |
| **Letter** | HTML-to-PDF (DinkToPdf/wkhtmltopdf) | `application/pdf` — Base64-encoded PDF bytes |
| **SMS** | HTML stripping + truncation to 160 chars | `text/plain` — plain text message |

For Letter channel previews, the Web UI provides a **Download PDF Preview** button. The PDF is delivered as a browser-side data URI (no server-side file is stored).

## Missing Placeholder Highlighting

When the sample data row does not contain a value for a placeholder used in the template, the preview:

1. Renders the placeholder as an **empty string** (so the rendering does not fail).
2. Lists the missing keys in the `missingPlaceholders` field of the response.
3. Displays a **warning banner** in the Web UI identifying the missing keys by name.

This helps designers identify which placeholders need data before going to production.

**Example warning:**
> Missing placeholder values (2): `code` `discount_pct` — These keys were in the template but had no value in the sample data row — rendered as blank.

## Business Rules

1. **Read-only**: Preview never sends messages. No `SEND_LOG` entries are created.
2. **Sample data limit**: At most 5 rows are fetched from the data source per preview call.
3. **Row selection**: The designer chooses which row (1–5) to render. Row 1 (index 0) is the default.
4. **Sub-templates resolved**: `{{> subtemplate_name}}` references are resolved recursively before rendering, just as they would be in a real send.
5. **Post-processing applied**: The rendered output goes through the same CSS inlining / PDF conversion / SMS truncation pipeline as a real send.
6. **Access control**: Only Designer and Admin roles can invoke preview (Business Rule BR-4).

## Troubleshooting

| Symptom | Likely Cause | Resolution |
|---------|-------------|------------|
| "The selected data source returned no rows" | The data source is empty or the query returned nothing. | Verify the data source has data. Check the connection string and table name. |
| "Failed to fetch sample data" | Database connection error. | Test the data source connection via **Admin > Data Sources > Test Connection**. |
| "Template rendering failed" | Scriban syntax error in the template body. | Review the template HTML for syntax issues using the placeholder extract endpoint or editor. |
| "Channel post-processing failed" | PDF conversion failure (Letter channel) or CSS inliner error. | Check the server logs. Ensure `libwkhtmltox.dll` is present for Letter channel. |
| Missing placeholder warning | Data source fields do not match template placeholder keys. | Update the placeholder manifest or align the data source field names with the template. |

## See Also

- [Template Lifecycle Guide](template-lifecycle-guide.md) — Draft, Published, Archived states.
- [Placeholder Syntax Guide](placeholder-syntax-guide.md) — Scriban `{{ key }}` syntax reference.
- [Sub-Template Composition Guide](sub-template-composition-guide.md) — `{{> name}}` inclusion syntax.
- [Data Source Configuration Guide](data-source-configuration-guide.md) — Setting up SQL Server data sources.
- [Channel Post-Processing](channel-post-processing.md) — Email CSS inlining, Letter PDF, SMS truncation.
