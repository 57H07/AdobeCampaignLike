# Letter Channel Configuration Guide

**TASK-021-08 | US-021 — PDF Letter Dispatcher**

---

## Overview

The Letter channel generates A4 portrait PDF files from rendered HTML content and delivers them by writing consolidated batch files to an output directory (UNC share or local path). A CSV manifest file is generated alongside each batch for print provider processing.

### Dispatch Flow

```
HTML Content
    |
    v
LetterPostProcessor (DinkToPdf / wkhtmltopdf)
    |
    v
Per-recipient PDF byte[]  ─────────────────────────┐
    |                                               |
    v (accumulated per campaign batch)              |
PdfConsolidationService (PdfSharp)                  |
    |                                               |
    v                                               |
Consolidated PDF batch files                        |
    + CSV manifest                                  |
    |                                               |
    v                                               |
PrintProviderFileDropHandler                        |
    |                                               v
OutputDirectory/CAMPAIGN_{id}_{ts}_{batch}.pdf      |
OutputDirectory/CAMPAIGN_{id}_{ts}_{batch}_manifest.csv
```

---

## Configuration

Add the `Letter` section to `appsettings.json`:

```json
{
  "Letter": {
    "IsEnabled": true,
    "OutputDirectory": "\\\\print-server\\letters\\incoming",
    "MaxPagesPerBatch": 500,
    "GenerateManifest": true,
    "FileNamePrefix": "CAMPAIGN",
    "WriteIndividualFiles": false
  }
}
```

### Configuration Properties

| Property             | Type    | Default      | Description |
|----------------------|---------|--------------|-------------|
| `IsEnabled`          | bool    | `true`       | Enable/disable the Letter channel. When false, all sends return a permanent failure without touching the file system. |
| `OutputDirectory`    | string  | `""`         | **Required.** Destination directory for PDF batch files and manifests. Supports UNC paths (`\\server\share`) and local paths. Created automatically if it does not exist. |
| `MaxPagesPerBatch`   | int     | `500`        | Maximum pages per consolidated PDF batch file. When exceeded, a new batch file is started. |
| `GenerateManifest`   | bool    | `true`       | Whether to generate a CSV manifest file for each PDF batch. |
| `FileNamePrefix`     | string  | `"CAMPAIGN"` | Prefix for output file names (business rule BR-4). |
| `WriteIndividualFiles` | bool  | `false`      | When true, write individual per-recipient PDFs in addition to consolidated batches. |

---

## PDF Generation

PDF generation uses **DinkToPdf** (wkhtmltopdf .NET wrapper) configured by the `LetterPostProcessor`.

### DinkToPdf Configuration (`LetterPostProcessor` section)

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

### Native Library Requirement

DinkToPdf requires `libwkhtmltox.dll` (x64, ~10 MB) to be present in the application root directory on Windows Server.

1. Download from: https://wkhtmltopdf.org/downloads.html
2. Place `libwkhtmltox.dll` (64-bit version) in the application root alongside `CampaignEngine.Web.exe`.

**Deployment checklist:**
- [ ] `libwkhtmltox.dll` (x64) present in application root
- [ ] Application pool runs as 64-bit
- [ ] Application pool identity has write access to `OutputDirectory`

---

## File Naming Convention

**Business rule BR-4:** All output files follow this naming pattern:

```
{FileNamePrefix}_{campaignId}_{timestamp}_{batchNumber}.pdf
{FileNamePrefix}_{campaignId}_{timestamp}_{batchNumber}_manifest.csv
```

**Example:**
```
CAMPAIGN_a1b2c3d4e5f6..._{yyyyMMddHHmmss}_001.pdf
CAMPAIGN_a1b2c3d4e5f6..._{yyyyMMddHHmmss}_001_manifest.csv
CAMPAIGN_a1b2c3d4e5f6..._{yyyyMMddHHmmss}_002.pdf
CAMPAIGN_a1b2c3d4e5f6..._{yyyyMMddHHmmss}_002_manifest.csv
```

---

## PDF Consolidation

**Business rule BR-5:** Maximum 500 pages per batch (configurable via `MaxPagesPerBatch`).

When a campaign has more than 500 pages of letters, the consolidation service splits them into multiple batch files. Pages are ordered by recipient insertion order (campaign sequence order, BR-2).

**Example:** 1,200 recipients × 1 page each = 3 batch files:
- Batch 001: recipients 1-500 (500 pages)
- Batch 002: recipients 501-1000 (500 pages)
- Batch 003: recipients 1001-1200 (200 pages)

---

## CSV Manifest Format

**Business rule BR-3:** Each PDF batch is accompanied by a CSV manifest file.

**Columns:**

| Column            | Description |
|-------------------|-------------|
| `SequenceInBatch` | 1-based position of the recipient within the campaign batch |
| `RecipientId`     | External reference ID from the data source |
| `DisplayName`     | Recipient display name |
| `PageCount`       | Number of pages this recipient's letter occupies |
| `BatchFileName`   | Name of the PDF batch file this recipient belongs to |

**Example manifest content:**

```csv
SequenceInBatch,RecipientId,DisplayName,PageCount,BatchFileName
1,REC-001,Alice Smith,1,CAMPAIGN_a1b2c3_20260320120000_001.pdf
2,REC-002,Bob Jones,2,CAMPAIGN_a1b2c3_20260320120000_001.pdf
3,REC-003,"Smith, Charlie",1,CAMPAIGN_a1b2c3_20260320120000_001.pdf
```

Fields containing commas, double-quotes, or newlines are wrapped in double-quotes (RFC 4180).

---

## Error Handling

| Scenario | Error Type | Behavior |
|----------|------------|----------|
| Channel disabled (`IsEnabled: false`) | Permanent | Returns failure; campaign continues for other recipients |
| Empty HTML content | Permanent | Returns failure; recipient is skipped |
| PDF generation failure (DinkToPdf unavailable) | Transient | Returns transient failure; Hangfire retries |
| Output directory not configured | Permanent | Throws `LetterDispatchException` during flush |
| UNC share temporarily unavailable | Transient | Throws `LetterDispatchException(isTransient: true)` during flush |
| PDF exceeds 10 MB limit | Permanent | Returns failure; recipient is skipped |

---

## Usage in LetterDispatcher

The `LetterDispatcher` is **stateful within a batch**. The dispatch flow is:

1. For each recipient: call `SendAsync(request)` to generate and accumulate the PDF.
2. Once all recipients are processed: call `FlushBatchAsync(campaignId)` to consolidate and write to disk.

```csharp
// Resolve from DI (Scoped — fresh instance per batch)
var dispatcher = serviceProvider.GetRequiredService<LetterDispatcher>();

// Step 1: generate individual PDFs
foreach (var recipient in recipients)
{
    var result = await dispatcher.SendAsync(new DispatchRequest
    {
        Channel = ChannelType.Letter,
        Content = renderedHtml,
        Recipient = new RecipientInfo { ExternalRef = recipient.Id, DisplayName = recipient.Name },
        CampaignId = campaign.Id
    });

    if (!result.Success)
        // log failure, continue with next recipient
}

// Step 2: consolidate and write to output directory
var writtenPaths = await dispatcher.FlushBatchAsync(campaign.Id);
// writtenPaths: ["\\print-server\letters\CAMPAIGN_..._001.pdf", ...]
```

---

## Open Questions

| # | Question | Impact |
|---|----------|--------|
| Q5 | Print provider format requirements | If the print provider requires API upload instead of file drop, replace `PrintProviderFileDropHandler.WriteAsync` with an HTTP POST implementation. |

When Q5 is resolved, create a new `ILetterProviderClient` abstraction and register a provider-specific implementation in DI, following the same strategy pattern used by `SmsProviderClient`.

---

## Deployment Requirements Summary

1. **Native library:** `libwkhtmltox.dll` (x64) in application root
2. **Output directory:** UNC share or local path, writable by application pool identity
3. **Disk space:** Allow ~2 MB per PDF page (compressed) for sizing estimates
4. **Configuration:** Set `Letter:OutputDirectory` in `appsettings.Production.json`
