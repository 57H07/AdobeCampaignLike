# Advanced Rendering Syntax Guide

**Engine:** Scriban 5.x
**Extends:** `docs/template-syntax-reference.md` (US-011 basic syntax)
**Implementation:** `ScribanTemplateRenderer` + `TemplateCustomFunctions` (US-012)

---

## Overview

This guide covers advanced template features built on top of the Scriban engine:

- Table rendering with row iteration
- List rendering (bulleted and numbered)
- Conditional blocks with boolean expressions
- Nested structures (table within conditional, conditional within loop)
- Custom functions: `format_date` and `format_currency`

---

## Table Rendering

### Syntax

```scriban
{{ for row in collection_name }}
<tr>
  <td>{{ row.field_one }}</td>
  <td>{{ row.field_two }}</td>
</tr>
{{ end }}
```

### Full Example: Order Lines Table

Template:

```scriban
<table>
  <thead>
    <tr>
      <th>Product</th>
      <th>Qty</th>
      <th>Unit Price</th>
      <th>Total</th>
    </tr>
  </thead>
  <tbody>
    {{ for row in order_lines }}
    <tr>
      <td>{{ row.product }}</td>
      <td>{{ row.quantity }}</td>
      <td>{{ format_currency row.unit_price "€" }}</td>
      <td>{{ format_currency row.total "€" }}</td>
    </tr>
    {{ end }}
  </tbody>
</table>
```

Data:

```json
{
  "order_lines": [
    { "product": "Widget A", "quantity": 2, "unit_price": 9.99, "total": 19.98 },
    { "product": "Widget B", "quantity": 1, "unit_price": 24.99, "total": 24.99 }
  ]
}
```

Output:

```html
<table>
  <thead>
    <tr>
      <th>Product</th><th>Qty</th><th>Unit Price</th><th>Total</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td>Widget A</td><td>2</td><td>€9.99</td><td>€19.98</td>
    </tr>
    <tr>
      <td>Widget B</td><td>1</td><td>€24.99</td><td>€24.99</td>
    </tr>
  </tbody>
</table>
```

### Row Index and First/Last Sentinels

```scriban
{{ for row in items }}
  {{ if for.first }}<tr class="first-row">{{ else }}<tr>{{ end }}
    <td>{{ for.index }}</td>
    <td>{{ row.name }}</td>
    {{ if for.last }}<td>LAST</td>{{ end }}
  </tr>
{{ end }}
```

| Variable     | Value                              |
|--------------|------------------------------------|
| `for.index`  | Zero-based row index (0, 1, 2, …) |
| `for.first`  | `true` for the first iteration     |
| `for.last`   | `true` for the last iteration      |
| `for.rindex` | Reverse index (last item = 0)      |

### Empty Table Handling

**Business rule:** Empty collections render nothing — no placeholder text, no empty tags.

```scriban
{{ for row in items }}
<tr><td>{{ row.name }}</td></tr>
{{ end }}
```

If `items` is an empty array or null, the loop body is skipped entirely. The wrapping `<table>` and `<tbody>` tags are outside the loop and will still appear; place them inside a conditional if you want to suppress them for empty collections (see [Table Within Conditional](#table-within-conditional)).

---

## List Rendering

### Bulleted List

```scriban
<ul>
{{ for item in features }}
  <li>{{ item }}</li>
{{ end }}
</ul>
```

Data:

```json
{ "features": ["Fast delivery", "Free returns", "24/7 support"] }
```

Output:

```html
<ul>
  <li>Fast delivery</li>
  <li>Free returns</li>
  <li>24/7 support</li>
</ul>
```

### Numbered List

```scriban
<ol>
{{ for step in steps }}
  <li>{{ step }}</li>
{{ end }}
</ol>
```

### Comma-Separated List (no trailing comma)

```scriban
{{ for item in tags }}{{ item }}{{ if !for.last }}, {{ end }}{{ end }}
```

Output: `Tag A, Tag B, Tag C`

### Empty List Handling

Same rule as tables: empty or null collections render no `<li>` elements.

```scriban
<ul>
{{ for item in items }}
<li>{{ item }}</li>
{{ end }}
</ul>
```

With `"items": []` — output is `<ul>\n</ul>` (empty but valid HTML).

---

## Conditional Blocks

### Basic If/End

```scriban
{{ if is_premium }}
<p>Thank you for being a <strong>Premium</strong> member.</p>
{{ end }}
```

### If/Else

```scriban
{{ if is_active }}
<p class="status-active">Account active</p>
{{ else }}
<p class="status-inactive">Account inactive</p>
{{ end }}
```

### If/Else If/Else Chain

```scriban
{{ if score >= 90 }}
<span class="grade-a">Grade A</span>
{{ else if score >= 70 }}
<span class="grade-b">Grade B</span>
{{ else if score >= 50 }}
<span class="grade-c">Grade C</span>
{{ else }}
<span class="grade-fail">Below passing</span>
{{ end }}
```

### Null / Empty Checks

```scriban
{{ if promo_code != null && promo_code != "" }}
<p>Use code <strong>{{ promo_code }}</strong> at checkout.</p>
{{ end }}
```

### Boolean Operators

| Operator | Example                                | Description               |
|----------|----------------------------------------|---------------------------|
| `&&`     | `{{ if is_active && has_plan }}`       | Logical AND               |
| `\|\|`   | `{{ if is_premium \|\| is_trial }}`    | Logical OR                |
| `!`      | `{{ if !is_blocked }}`                 | Logical NOT               |
| `==`     | `{{ if status == "active" }}`          | Equality                  |
| `!=`     | `{{ if status != "cancelled" }}`       | Inequality                |
| `>`      | `{{ if balance > 0 }}`                 | Greater than              |
| `<`      | `{{ if age < 18 }}`                    | Less than                 |
| `>=`     | `{{ if score >= 90 }}`                 | Greater than or equal     |
| `<=`     | `{{ if score <= 100 }}`                | Less than or equal        |

**Note:** Data string values are HTML-encoded before substitution.
When comparing a data field to a string literal in a conditional, use unencoded values.
Example: `{{ if status == "active" }}` — the literal `"active"` is not encoded, but the data value `status` is compared after encoding. For plain ASCII strings with no HTML characters, comparisons work as expected.

---

## Nested Structures

### Table Within Conditional

Use a conditional to suppress the entire table (including headers) when the collection is empty.

```scriban
{{ if has_orders }}
<table>
  <thead>
    <tr><th>Reference</th><th>Amount</th></tr>
  </thead>
  <tbody>
    {{ for order in orders }}
    <tr>
      <td>{{ order.ref }}</td>
      <td>{{ format_currency order.amount "€" }}</td>
    </tr>
    {{ end }}
  </tbody>
</table>
{{ else }}
<p>No orders found.</p>
{{ end }}
```

Data (with orders):

```json
{
  "has_orders": true,
  "orders": [
    { "ref": "ORD-001", "amount": 99.99 },
    { "ref": "ORD-002", "amount": 49.50 }
  ]
}
```

### Conditional Within Loop

Apply per-row conditional logic to highlight new items, show/hide columns, or apply CSS classes.

```scriban
{{ for item in products }}
<tr{{ if item.is_new }} class="new-product"{{ end }}>
  <td>{{ item.name }}</td>
  <td>{{ format_currency item.price "€" }}</td>
  {{ if item.is_new }}
  <td><span class="badge">NEW</span></td>
  {{ else }}
  <td></td>
  {{ end }}
</tr>
{{ end }}
```

### List Within Conditional

```scriban
{{ if has_features }}
<ul>
{{ for feature in features }}
<li>{{ feature }}</li>
{{ end }}
</ul>
{{ end }}
```

---

## Custom Functions

### `format_date`

Formats a date value using a .NET format string.

**Signature:** `format_date(value, format)`

| Parameter | Type                       | Description                         |
|-----------|----------------------------|-------------------------------------|
| `value`   | DateTime, string, or null  | The date to format                  |
| `format`  | string                     | .NET date format string             |

**Usage:**

```scriban
{{ format_date invoice_date "dd/MM/yyyy" }}        -> 19/03/2026
{{ format_date birth_date "MMMM d, yyyy" }}        -> March 19, 1990
{{ format_date created_at "yyyy-MM-dd HH:mm" }}    -> 2026-03-19 14:30
```

**Common format strings:**

| Format string      | Example output      |
|--------------------|---------------------|
| `"dd/MM/yyyy"`     | 19/03/2026          |
| `"MM/dd/yyyy"`     | 03/19/2026          |
| `"yyyy-MM-dd"`     | 2026-03-19          |
| `"MMMM d, yyyy"`   | March 19, 2026      |
| `"d MMMM yyyy"`    | 19 March 2026       |
| `"dd MMM yy"`      | 19 Mar 26           |

**Behaviour for null/missing values:** Returns empty string.

**String input support:** ISO 8601 date strings are automatically parsed.

```scriban
{{ format_date "2026-03-19" "dd/MM/yyyy" }}    -> 19/03/2026
```

### `format_currency`

Formats a numeric value as currency with an optional prefix symbol.

**Signature:** `format_currency(value, symbol)`

| Parameter | Type            | Description                                         |
|-----------|-----------------|-----------------------------------------------------|
| `value`   | decimal or null | The amount to format                                |
| `symbol`  | string          | Prefix symbol (e.g. "€", "$"). Use "" for no prefix |

**Usage:**

```scriban
{{ format_currency total "€" }}       -> €1,234.56
{{ format_currency price "$" }}       -> $9.99
{{ format_currency amount "£" }}      -> £500.00
{{ format_currency value "" }}        -> 1,234.56
```

**Formatting rules:**
- Always uses 2 decimal places
- Uses comma (`,`) as thousands separator and dot (`.`) as decimal separator (InvariantCulture)
- Symbol is prepended directly without a space

**Behaviour for null/missing values:** Returns empty string.

**Example with loop:**

```scriban
{{ for line in invoice_lines }}
<tr>
  <td>{{ line.description }}</td>
  <td style="text-align:right">{{ format_currency line.amount "€" }}</td>
</tr>
{{ end }}
<tr>
  <td><strong>Total</strong></td>
  <td style="text-align:right"><strong>{{ format_currency grand_total "€" }}</strong></td>
</tr>
```

---

## Complete Email Template Example

This example combines all advanced features:

```scriban
<p>Dear {{ first_name }} {{ last_name }},</p>

<p>Invoice date: {{ format_date invoice_date "dd/MM/yyyy" }}</p>
<p>Due date: {{ format_date due_date "dd/MM/yyyy" }}</p>

{{ if is_overdue }}
<p style="color:red"><strong>OVERDUE:</strong> Payment is past due.</p>
{{ end }}

{{ if has_discount }}
<p>Loyalty discount applied: {{ discount_percent }}%</p>
{{ end }}

{{ if order_lines }}
<table border="1" cellpadding="4">
  <thead>
    <tr>
      <th>Description</th>
      <th>Qty</th>
      <th>Unit Price</th>
      <th>Line Total</th>
    </tr>
  </thead>
  <tbody>
    {{ for line in order_lines }}
    <tr{{ if line.is_promotional }} style="background:#fffbe6"{{ end }}>
      <td>{{ line.description }}</td>
      <td>{{ line.quantity }}</td>
      <td>{{ format_currency line.unit_price "€" }}</td>
      <td>{{ format_currency line.total "€" }}</td>
    </tr>
    {{ end }}
  </tbody>
</table>

<p><strong>Total: {{ format_currency grand_total "€" }}</strong></p>
{{ else }}
<p>No items on this invoice.</p>
{{ end }}

{{ if attachments }}
<p>Attached documents:</p>
<ul>
{{ for doc in attachments }}
<li>{{ doc }}</li>
{{ end }}
</ul>
{{ end }}

<p>Thank you for your business.</p>
```

---

## Data Mapping Reference

| Template pattern                    | Required data shape                                      |
|-------------------------------------|----------------------------------------------------------|
| `{{ for row in collection }}`       | `"collection": [ {...}, {...} ]` — array of objects      |
| `{{ for item in list }}`            | `"list": ["a", "b", "c"]` — array of scalars            |
| `{{ if bool_flag }}`                | `"bool_flag": true` or `false`                           |
| `{{ format_date field "fmt" }}`     | `"field": "2026-03-19"` or a .NET DateTime object        |
| `{{ format_currency field "€" }}`  | `"field": 1234.56` — numeric (int, decimal, double)      |

---

## Error Handling Summary

| Scenario                          | Behaviour                                      |
|-----------------------------------|------------------------------------------------|
| Empty collection in `for` loop    | Loop body not rendered (no output)             |
| Null variable in `for` loop       | Loop body not rendered (no output)             |
| Null in `format_date`             | Returns empty string                           |
| Null in `format_currency`         | Returns empty string                           |
| Unparseable string in `format_date` | Returns empty string (no exception)          |
| Non-numeric in `format_currency`  | Returns empty string (no exception)            |
| Missing variable in `{{ if }}`    | Treated as falsy (no exception)                |

---

## Further Reading

- Basic syntax: `docs/template-syntax-reference.md`
- `ITemplateRenderer`: `src/CampaignEngine.Application/Interfaces/ITemplateRenderer.cs`
- `TemplateCustomFunctions`: `src/CampaignEngine.Infrastructure/Rendering/TemplateCustomFunctions.cs`
- `ScribanTemplateRenderer`: `src/CampaignEngine.Infrastructure/Rendering/ScribanTemplateRenderer.cs`
- [Scriban official documentation](https://github.com/scriban/scriban/blob/master/doc/language.md)
