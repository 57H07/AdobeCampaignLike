# Channel Post-Processing

**US-013 — Rendering Engine**

This document describes how CampaignEngine transforms rendered HTML template output into
the correct format for each communication channel. Post-processing runs after Scriban
template rendering and before dispatch.

---

## Architecture Overview

Post-processing uses the same DI-based strategy pattern as channel dispatchers.

```
ITemplateRenderer.RenderAsync()
        ↓ (rendered HTML)
IChannelPostProcessorRegistry.GetProcessor(channel)
        ↓
IChannelPostProcessor.ProcessAsync()
        ↓ (PostProcessingResult)
IChannelDispatcher.SendAsync()
```

### Key types

| Type | Layer | Purpose |
|------|-------|---------|
| `IChannelPostProcessor` | Application | Strategy interface |
| `IChannelPostProcessorRegistry` | Application | Resolves processor by ChannelType |
| `PostProcessingResult` | Application | Output: text or binary content |
| `PostProcessingContext` | Application | Optional metadata (campaign ID, base URL, SMS limit) |
| `EmailPostProcessor` | Infrastructure | CSS inlining via PreMailer.Net |
| `LetterPostProcessor` | Infrastructure | HTML→PDF via DinkToPdf (wkhtmltopdf) |
| `SmsPostProcessor` | Infrastructure | HTML stripping + truncation |
| `PdfConsolidationService` | Infrastructure | Multi-PDF merging via PdfSharp |
| `PostProcessingException` | Domain | Failure with `IsTransient` flag |

---

## Email Channel

### Purpose

Email clients — especially Microsoft Outlook — do not support CSS stylesheets embedded in
`<style>` blocks or linked via `<link>` tags. CSS rules must appear as inline `style="..."`
attributes on every HTML element. PreMailer.Net performs this transformation automatically.

### What happens

1. All CSS rules in `<style>` blocks are moved to `style=""` attributes on matching elements.
2. The `<style>` blocks are removed from the document (reduces message size).
3. Pre-existing inline styles are merged with rules from the style block.
4. Warnings are logged (not thrown) for unsupported CSS selectors.

### Configuration

No additional configuration is required. The processor is registered automatically.

### Outlook compatibility tips

- Use `<table>`-based layouts for complex multi-column designs.
- Avoid CSS Grid and Flexbox — Outlook does not support them.
- Use `mso-` prefixed properties for Outlook-specific overrides (these are preserved).
- Test rendered output with [Litmus](https://litmus.com) or [Email on Acid](https://www.emailonacid.com).

### Example

Input HTML:
```html
<html>
<head>
  <style>
    p { color: #333; font-family: Arial, sans-serif; }
    .header { background-color: #003366; color: white; }
  </style>
</head>
<body>
  <div class="header">Welcome</div>
  <p>Your message here.</p>
</body>
</html>
```

Post-processed output:
```html
<html>
<head></head>
<body>
  <div class="header" style="background-color: #003366; color: white;">Welcome</div>
  <p style="color: #333; font-family: Arial, sans-serif;">Your message here.</p>
</body>
</html>
```

---

## Letter Channel

### Purpose

Letter campaigns generate A4 PDF documents from rendered HTML. These PDFs are sent to a
print provider who physically mails them to recipients.

### PDF tool selection (POC result — TASK-013-01)

**DinkToPdf** was selected after evaluating three options:

| Tool | Decision | Reason |
|------|----------|--------|
| **DinkToPdf** | **CHOSEN** | In-process (no subprocess), faithful WebKit rendering, Windows IIS compatible, well-maintained .NET wrapper |
| wkhtmltopdf CLI | Rejected | Requires subprocess spawn, harder deployment, same underlying engine as DinkToPdf |
| Puppeteer/Chrome | Rejected | Requires ~150 MB Chromium download, complex IIS deployment, overkill for server-side PDF |

### Native library deployment

DinkToPdf is a .NET P/Invoke wrapper around `libwkhtmltox.dll` (Windows) / `libwkhtmltox.so` (Linux).

**Required deployment step:**
1. Download `wkhtmltox` 0.12.6 (x64) from https://wkhtmltopdf.org/downloads.html
2. Extract `libwkhtmltox.dll` (Windows) or `libwkhtmltox.so` (Linux)
3. Place the native library in the application root directory (next to the `.exe`)

On IIS, the application pool identity must have read access to this file.

### Format and constraints

| Property | Value |
|----------|-------|
| Paper format | A4 (210 × 297 mm) |
| Orientation | Portrait |
| Margins | Top: 20 mm, Bottom: 20 mm, Left: 25 mm, Right: 25 mm |
| DPI | 96 (configurable) |
| Max file size | 10 MB per document |
| Encoding | UTF-8 |

### Configuration (appsettings.json)

```json
{
  "LetterPostProcessor": {
    "MarginTopMm": 20,
    "MarginBottomMm": 20,
    "MarginLeftMm": 25,
    "MarginRightMm": 25,
    "Dpi": 96
  }
}
```

### Error handling

| Condition | Exception type | IsTransient |
|-----------|---------------|-------------|
| Empty HTML input | `PostProcessingException` | false |
| Native library not found | `PostProcessingException` | true |
| Converter returns empty result | `PostProcessingException` | true |
| PDF exceeds 10 MB limit | `PostProcessingException` | false |

---

## SMS Channel

### Purpose

SMS messages are plain text only. The post-processor strips all HTML markup from the
rendered output and truncates the result to fit within the GSM-7 single-message limit
of 160 characters.

### What happens

1. HTML is parsed using HtmlAgilityPack.
2. `<script>` and `<style>` elements are completely removed (including their text content).
3. Inner text is extracted from all remaining elements.
4. HTML entities are decoded (`&amp;` → `&`, `&lt;` → `<`, etc.).
5. Runs of whitespace (spaces, tabs, newlines) are collapsed to a single space.
6. The result is trimmed and truncated to 160 characters (or custom limit).

### Truncation algorithm

Truncation preserves whole words where possible:

1. If the content is at or below the limit, it is returned unchanged.
2. If the character at position `limit` is a space (or end-of-string), the cut falls on
   a word boundary — return the first `limit` characters unchanged.
3. Otherwise (mid-word cut), back up to the last space before the limit.
4. If no space exists within the limit, hard-truncate at exactly `limit` characters.

### Custom SMS length

The default limit of 160 characters can be overridden per message via `PostProcessingContext`:

```csharp
var context = new PostProcessingContext { SmsMaxLength = 70 }; // GSM-7 concatenated segment
var result = await processor.ProcessAsync(html, context);
```

### Example

Input:
```html
<html><body>
  <p>Dear <strong>Alice</strong>,</p>
  <p>Your order <a href="...">REF-001</a> has been shipped.</p>
</body></html>
```

Output (plain text):
```
Dear Alice, Your order REF-001 has been shipped.
```

---

## PDF Consolidation (Letter batches)

For letter campaigns with many recipients, individual PDFs are merged into batch files
for delivery to the print provider. `PdfConsolidationService` handles this using PdfSharp
(pure .NET, no native dependencies).

### Business rule (BR-4)

**Maximum 500 pages per batch file.**

When a consolidated batch would exceed 500 pages, a new batch file is started.

### Example: 1,200 recipients with 1-page PDFs

- Batch 1: pages 1–500 (recipients 1–500)
- Batch 2: pages 501–1000 (recipients 501–1000)
- Batch 3: pages 1001–1200 (recipients 1001–1200)

### Usage

```csharp
// individual PDFs from LetterPostProcessor
var individualPdfs = new List<byte[]> { pdf1, pdf2, ..., pdf1200 };

IReadOnlyList<byte[]> batches = await consolidationService.ConsolidateAsync(individualPdfs);

foreach (var (batch, index) in batches.Select((b, i) => (b, i)))
{
    var fileName = $"CAMPAIGN_{campaignId}_{timestamp}_batch{index + 1:D3}.pdf";
    // transmit batch to print provider file drop
}
```

### Error handling

- Invalid (corrupt) PDF documents are skipped with a warning log. The batch continues.
- Null or empty byte arrays in the input list are skipped.
- Cancellation is supported via `CancellationToken`.

---

## Adding a new channel post-processor

The registry uses the DI strategy pattern — no switch/case statements required.

1. Create a class implementing `IChannelPostProcessor`:

```csharp
public class WhatsAppPostProcessor : IChannelPostProcessor
{
    public ChannelType Channel => ChannelType.WhatsApp;

    public Task<PostProcessingResult> ProcessAsync(
        string renderedHtml,
        PostProcessingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        // Transform HTML for WhatsApp (e.g., markdown-style formatting)
        var result = ConvertToWhatsAppFormat(renderedHtml);
        return Task.FromResult(PostProcessingResult.Text(result, "text/plain"));
    }
}
```

2. Register in `ServiceCollectionExtensions.cs`:

```csharp
services.AddScoped<IChannelPostProcessor, WhatsAppPostProcessor>();
```

3. No changes required to the registry or orchestration layer.

---

## Error handling summary

All `IChannelPostProcessor` implementations throw `PostProcessingException` on failure:

```csharp
try
{
    var result = await registry.GetProcessor(channel).ProcessAsync(html, context);
    // use result
}
catch (PostProcessingException ex) when (ex.IsTransient)
{
    // Retry — infrastructure problem (PDF engine unavailable, etc.)
}
catch (PostProcessingException ex)
{
    // Permanent failure — log and mark as failed (e.g., oversized PDF, malformed input)
    logger.LogError(ex, "Post-processing failed permanently for channel {Channel}", ex.Channel);
}
```
