# Template Syntax Reference

**Engine:** Scriban 5.x (Liquid-like syntax)
**Interface:** `ITemplateRenderer` (Application layer)
**Implementation:** `ScribanTemplateRenderer` (Infrastructure layer)

---

## Overview

Templates use Scriban syntax — a lightweight, sandboxed, Liquid-compatible language.
Double curly-braces `{{ }}` delimit expressions. All other content is rendered verbatim.

Security model:
- All **data values** are HTML-encoded by default (prevents XSS).
- **Template HTML** is trusted (only users with the Designer role can create/edit templates).
- Engine is sandboxed: no file system access, no .NET reflection, no shell execution.
- Render timeout: 10 seconds per operation (configurable via `TemplateContext.Timeout`).

---

## Basic Scalar Substitution

Replace a named placeholder with a value from the data dictionary.

```scriban
Dear {{ first_name }} {{ last_name }},

Your account {{ account_id }} has a balance of {{ balance }}.
```

Data:

```json
{
  "first_name": "Alice",
  "last_name": "Dupont",
  "account_id": "ACC-001",
  "balance": 1234.56
}
```

Output:

```
Dear Alice Dupont,

Your account ACC-001 has a balance of 1234.56.
```

**Notes:**
- Variable names are **case-insensitive**: `{{ FirstName }}` and `{{ first_name }}` are equivalent.
- Missing variables render as **empty string** (no error, no placeholder visible).
- String values are **HTML-encoded** automatically (e.g., `<b>` becomes `&lt;b&gt;`).

---

## Iteration (Tables and Lists)

Use `for...in...end` to iterate over a collection.

### HTML table from array of objects

```scriban
<table>
  <thead>
    <tr><th>Product</th><th>Qty</th><th>Price</th></tr>
  </thead>
  <tbody>
    {{ for row in order_lines }}
    <tr>
      <td>{{ row.product }}</td>
      <td>{{ row.quantity }}</td>
      <td>{{ row.unit_price }}</td>
    </tr>
    {{ end }}
  </tbody>
</table>
```

Data:

```json
{
  "order_lines": [
    { "product": "Widget A", "quantity": 2, "unit_price": 9.99 },
    { "product": "Widget B", "quantity": 1, "unit_price": 24.99 }
  ]
}
```

### Bulleted list from scalar array

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

**Notes:**
- Empty collections render nothing (no empty `<ul>`, no placeholder text).
- Use `for.index` for the zero-based iteration counter.
- Use `for.first` / `for.last` for conditional first/last-item formatting.

---

## Conditional Blocks

Use `if / else if / else / end` for conditional content.

```scriban
{{ if is_premium }}
  <p>Thank you for being a <strong>Premium</strong> member.</p>
{{ else if is_active }}
  <p>Welcome back, valued customer.</p>
{{ else }}
  <p>Create an account to unlock exclusive benefits.</p>
{{ end }}
```

Data:

```json
{ "is_premium": true, "is_active": false }
```

### Checking nulls and empty strings

```scriban
{{ if promo_code != null && promo_code != "" }}
  <p>Use code <strong>{{ promo_code }}</strong> at checkout.</p>
{{ end }}
```

---

## Nested Objects

Access nested properties using dot notation.

```scriban
Shipping to: {{ address.street }}, {{ address.city }} {{ address.postal_code }}
```

Data:

```json
{
  "address": {
    "street": "12 Rue de la Paix",
    "city": "Paris",
    "postal_code": "75001"
  }
}
```

---

## Filters (Built-in String Formatting)

Scriban supports pipe filters for common transformations.

```scriban
{{ name | string.upcase }}          -> ALICE DUPONT
{{ name | string.downcase }}        -> alice dupont
{{ name | string.capitalize }}      -> Alice dupont
{{ description | string.truncate 50 "..." }}
```

**Date formatting** (when passing a .NET `DateTime` object):

```scriban
{{ sent_date | date.to_string "%d/%m/%Y" }}  -> 19/03/2026
```

**Number formatting** (use .NET format strings):

```scriban
{{ balance | math.format "N2" }}   -> 1,234.56
```

---

## Custom Functions (Email/SMS Only)

The following built-in helper functions are registered automatically in the Scriban rendering
context for **Email and SMS** channel templates. They are **not available** in Letter/DOCX
templates, which use plain-text `{{ }}` placeholder substitution (see F-305).

### `format_date`

Formats a date value using a .NET format string.

```scriban
{{ format_date invoice_date "dd/MM/yyyy" }}    -> 19/03/2026
{{ format_date birth_date "MMMM d, yyyy" }}   -> March 19, 1990
```

- Accepts `DateTime`, `DateTimeOffset`, `DateOnly`, or ISO 8601 string inputs.
- Null or unparseable values return **empty string** (no exception).
- Format strings follow .NET standard date format specifiers.

### `format_currency`

Formats a numeric value as currency with a prefix symbol.

```scriban
{{ format_currency amount "€" }}    -> €1,234.56
{{ format_currency price "$" }}     -> $9.99
{{ format_currency total "" }}      -> 1,234.56
```

- Always uses 2 decimal places and invariant-culture separators (`.` decimal, `,` thousands).
- Symbol is prepended directly without a space (business rule BR-019-02).
- Null or non-numeric values return **empty string** (no exception).

> For detailed examples and loop usage, see `docs/advanced-rendering-syntax.md`.

---

## String Concatenation

```scriban
{{ "Hello, " + first_name + "!" }}
```

---

## Comments

```scriban
{{# This is a comment — not rendered in output }}
```

---

## Whitespace Control

Append `-` inside `{{-` or `-}}` to strip surrounding whitespace/newlines.

```scriban
{{- for item in items -}}
  {{ item }}
{{- end -}}
```

---

## HTML Encoding Behaviour

By default, **all data values** are HTML-encoded before substitution.

| Input value      | Rendered output     |
|------------------|---------------------|
| `<b>bold</b>`    | `&lt;b&gt;bold&lt;/b&gt;` |
| `Tom & Jerry`    | `Tom &amp; Jerry`   |
| `"quoted"`       | `&quot;quoted&quot;` |

Template **HTML structure** (tags, attributes) is **not** encoded — it is rendered verbatim.

To render unencoded HTML from data (e.g. a pre-sanitized rich-text field), set
`TemplateContext.HtmlEncodeValues = false`. Only do this when the data source is trusted.

---

## Security Sandbox

The following operations are **disabled** in the sandbox:

- File system access (`io.*` functions)
- .NET reflection or object introspection
- Shell execution
- Network calls
- Loop iterations beyond 10,000 (throws render error)
- Object recursion beyond 64 levels (throws render error)

Templates may only access values explicitly provided in the data dictionary.

---

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Syntax error in template | `TemplateRenderException` thrown with location |
| Missing variable | Renders as empty string (no exception) |
| Render timeout (> 10s) | `TemplateRenderException` thrown with timeout message |
| Loop exceeds 10,000 iterations | `TemplateRenderException` thrown |
| Null value in data | Renders as empty string |

---

## Integration Example (C#)

```csharp
// Inject via DI
public class EmailService(ITemplateRenderer renderer)
{
    public async Task<string> RenderEmailAsync(string templateBody, Dictionary<string, object?> data)
    {
        // Simple usage
        return await renderer.RenderAsync(templateBody, data);
    }

    public async Task<string> RenderSmsAsync(string templateBody, Dictionary<string, object?> data)
    {
        // For SMS: disable HTML encoding (plain text channel)
        var context = new TemplateContext
        {
            Data = data,
            HtmlEncodeValues = false,
            Timeout = TimeSpan.FromSeconds(5)
        };
        return await renderer.RenderAsync(templateBody, context);
    }
}
```

---

## Performance Characteristics

- **Throughput:** > 1,000 renders/second on a single thread (scalar templates)
- **Latency:** < 5ms per render (p99, scalar templates)
- **Thread safety:** Fully stateless — safe for concurrent use from multiple threads
- **Memory:** Template is parsed fresh each call; no long-lived template cache (simplicity over micro-optimization)

---

## Further Reading

- [Scriban official documentation](https://github.com/scriban/scriban/blob/master/doc/language.md)
- `ITemplateRenderer` interface: `src/CampaignEngine.Application/Interfaces/ITemplateRenderer.cs`
- `TemplateContext` model: `src/CampaignEngine.Application/Models/TemplateContext.cs`
- `ScribanTemplateRenderer`: `src/CampaignEngine.Infrastructure/Rendering/ScribanTemplateRenderer.cs`
- Custom functions (advanced usage): `docs/advanced-rendering-syntax.md`
- `FormatDateFunction`: `src/CampaignEngine.Application/Rendering/CustomFunctions/FormatDateFunction.cs`
- `FormatCurrencyFunction`: `src/CampaignEngine.Application/Rendering/CustomFunctions/FormatCurrencyFunction.cs`
