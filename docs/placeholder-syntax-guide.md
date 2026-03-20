# Placeholder Syntax Guide

## Overview

Templates in CampaignEngine use **Scriban** as the template engine. Placeholders are declared in the template HTML body using Scriban syntax. Each placeholder must be declared in the template's **Placeholder Manifest** before the template can be published.

The manifest specifies:
- The placeholder **key**
- The **type** (Scalar, Table, List, FreeField)
- The **source** (Data Source or Operator Input)

---

## Placeholder Types

### Scalar

A simple value: string, number, date, or boolean.

**Syntax:** `{{ key }}`

**Example:**
```html
<p>Dear {{ customerName }},</p>
<p>Your invoice dated {{ invoiceDate }} is due.</p>
<p>Total amount: {{ totalAmount }}</p>
```

**Manifest declaration:**
```json
{ "key": "customerName", "type": "Scalar", "isFromDataSource": true }
{ "key": "invoiceDate",  "type": "Scalar", "isFromDataSource": true }
{ "key": "totalAmount",  "type": "Scalar", "isFromDataSource": true }
```

---

### Table

An iterable collection of rows (objects with named fields).

**Syntax:** `{{ for alias in collectionKey }} ... {{ end }}`

The **manifest key** is the collection variable name (`collectionKey`), not the alias.

**Example:**
```html
<table>
  <thead><tr><th>Product</th><th>Qty</th><th>Price</th></tr></thead>
  <tbody>
    {{ for line in orderLines }}
    <tr>
      <td>{{ line.productName }}</td>
      <td>{{ line.quantity }}</td>
      <td>{{ line.unitPrice }}</td>
    </tr>
    {{ end }}
  </tbody>
</table>
```

**Manifest declaration:**
```json
{ "key": "orderLines", "type": "Table", "isFromDataSource": true }
```

> The loop alias (`line`) is a local variable and must NOT be declared in the manifest.

---

### List

An iterable collection of simple values (strings, numbers).

**Syntax:** `{{ for item in listKey }} ... {{ end }}`

**Example:**
```html
<ul>
  {{ for tag in categories }}
  <li>{{ tag }}</li>
  {{ end }}
</ul>
```

**Manifest declaration:**
```json
{ "key": "categories", "type": "List", "isFromDataSource": true }
```

---

### FreeField

A value that must be provided by the **campaign operator** at campaign creation time. Not fetched from the data source.

**Syntax:** Same as Scalar — `{{ key }}`

**Example:**
```html
<p>Subject: {{ campaignTitle }}</p>
<p>{{ personalizedMessage }}</p>
```

**Manifest declaration:**
```json
{ "key": "campaignTitle",       "type": "FreeField", "isFromDataSource": false }
{ "key": "personalizedMessage", "type": "FreeField", "isFromDataSource": false }
```

> FreeField placeholders always have `isFromDataSource: false`. The platform enforces this automatically.

---

## Source Indication

Every manifest entry declares where the value comes from:

| Source           | `isFromDataSource` | Description |
|------------------|--------------------|-------------|
| **Data Source**  | `true`             | Value fetched from the configured SQL Server / REST API data connector |
| **Operator Input** | `false`          | Value manually entered by the campaign operator at campaign creation time |

---

## Conditionals

Conditional blocks use Scriban's `{{ if }}` syntax. Conditional expressions themselves are not placeholders and do not need to be declared:

```html
{{ if isPremiumCustomer }}
<p>As a premium customer, you receive an extra 10% discount.</p>
{{ end }}
```

If `isPremiumCustomer` is a data-source field used in the condition, declare it as a Scalar:

```json
{ "key": "isPremiumCustomer", "type": "Scalar", "isFromDataSource": true }
```

---

## Manifest Validation Rules

1. **All placeholders in the HTML body must be declared.** A template with undeclared placeholders cannot be published.
2. **Placeholder keys must be unique** within a template's manifest.
3. **FreeField type** forces `isFromDataSource = false` regardless of what is submitted.
4. **DataSource placeholders** must map to fields available in the assigned data source at campaign execution time.
5. **Orphan manifest entries** (declared but not present in the HTML body) are informational warnings, not errors.

---

## API Reference

| Endpoint | Description |
|----------|-------------|
| `GET /api/templates/{id}/placeholders` | List all declared manifest entries |
| `POST /api/templates/{id}/placeholders` | Add a single manifest entry |
| `PUT /api/templates/{id}/placeholders/{entryId}` | Update a manifest entry |
| `DELETE /api/templates/{id}/placeholders/{entryId}` | Remove a manifest entry |
| `PUT /api/templates/{id}/placeholders/bulk` | Replace entire manifest atomically |
| `GET /api/templates/{id}/placeholders/extract` | Auto-detect placeholders from HTML (read-only) |
| `GET /api/templates/{id}/placeholders/validate` | Validate manifest completeness |

---

## UI Workflow

1. Open the template in the **Template Editor** (Edit page).
2. Click **Edit Manifest** on the detail page to open the manifest editor.
3. Use **Auto-detect from HTML** to scan the template body and suggest missing declarations.
4. For each detected placeholder, review the type and source, then click **Add**.
5. Save the manifest with **Save Manifest**.
6. The detail page shows a **Complete** badge when all placeholders are declared.

---

## Scriban Reference

- [Scriban Language Documentation](https://github.com/scriban/scriban/blob/master/doc/language.md)
- Template engine is sandboxed: no file I/O, no arbitrary code execution
- All substituted values are HTML-escaped by default (XSS prevention)
- Template HTML itself is trusted — only Designer and Admin roles can create/edit templates
