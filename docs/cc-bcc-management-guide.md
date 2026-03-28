# CC and BCC Management Guide

This guide explains how to configure CC and BCC recipients for email campaigns in CampaignEngine.

## Overview

CampaignEngine supports two types of CC recipients:

- **Static CC** — operator-defined email addresses applied to every send in the campaign
- **Dynamic CC** — per-recipient CC address read from a data source field
- **Static BCC** — hidden copies sent to compliance or audit addresses

CC/BCC configuration is set at campaign creation and applies to all email steps in the campaign.

## Business Rules

| Rule | Description |
|------|-------------|
| BR-1 | Static CC: comma-separated email list, max 2000 characters |
| BR-2 | Dynamic CC: data field containing email or semicolon/comma-separated list |
| BR-3 | Invalid emails are **logged** but do **not** fail the send |
| BR-4 | Maximum **10 CC recipients** per send (static + dynamic combined) |
| BR-5 | Deduplication is case-insensitive — the same address receives only one copy |

## Configuration Options

### Static CC Addresses

Specified as a comma-separated list at campaign creation. Applied to every email in the campaign, regardless of the recipient.

**Example:**

```
campaign-owner@company.com, compliance@company.com
```

**API request field:** `staticCcAddresses` (string, max 2000 characters)

### Dynamic CC Field

A data source field name whose value is used as a per-recipient CC address. The field can contain:

- A single email address: `account_manager@company.com`
- A semicolon-separated list: `manager@company.com;supervisor@company.com`
- A comma-separated list: `manager@company.com,supervisor@company.com`

**API request field:** `dynamicCcField` (string, max 200 characters)

**Example data source row:**

| email | cc_contact |
|-------|-----------|
| john@customer.com | account_mgr@company.com |
| jane@customer.com | regional_rep@company.com;national_lead@company.com |

With `dynamicCcField = "cc_contact"`, each send includes the corresponding CC from the row.

### Static BCC Addresses

Hidden copies sent to addresses that are not visible to the To or CC recipients. Used for compliance, audit, or monitoring purposes.

**Example:**

```
audit-trail@company.com
```

**API request field:** `staticBccAddresses` (string, max 2000 characters)

## Deduplication

The CC resolution service deduplicates all addresses (static + dynamic combined) before sending:

- Comparison is **case-insensitive**: `CC@EXAMPLE.COM` and `cc@example.com` are treated as the same address
- The **first occurrence** is retained (preserving original casing)
- Deduplication happens after validation — invalid addresses are removed before deduplication

**Example:**

Static CC: `manager@company.com, MANAGER@COMPANY.COM`
Dynamic CC: `manager@company.com`

Result: `["manager@company.com"]` (one address, deduplicated)

## Recipient Cap (Max 10)

If the combined CC list (after validation and deduplication) exceeds 10 recipients, the list is **truncated to the first 10 addresses**. A warning is logged when this occurs.

Static CC entries are processed first, dynamic CC second. If you need to prioritize certain addresses, list them first in the static CC field.

## Email Validation

All CC and BCC addresses are validated using RFC 2822-compatible parsing (via MimeKit). Invalid addresses are:

1. **Logged** at Warning level with the context ("StaticCC", "DynamicCC", "StaticBCC")
2. **Skipped** — the send continues without the invalid address
3. **Not counted** against the 10-recipient cap

**Valid examples:**

- `user@example.com`
- `user.name+tag@sub.domain.co.uk`
- `First.Last@example.com`

**Invalid examples (will be skipped):**

- `@nodomain` — missing local part
- `two@@signs.com` — double @ sign
- `test@` — missing domain

## Campaign Wizard UI

CC/BCC configuration is available in **Step 5** of the campaign creation wizard, under the **CC / BCC Configuration** section.

| Field | Description |
|-------|-------------|
| Static CC Addresses | Comma-separated email list |
| Dynamic CC Field | Data source field name (e.g., `account_manager_email`) |
| Static BCC Addresses | Comma-separated email list for hidden copies |

The Campaign Summary shown before submission includes the CC/BCC configuration for review.

## API Usage

### Create campaign with CC/BCC

`POST /api/campaigns`

```json
{
  "name": "Q1 Newsletter",
  "dataSourceId": "3fa85f64-...",
  "staticCcAddresses": "manager@company.com, compliance@company.com",
  "dynamicCcField": "account_manager_email",
  "staticBccAddresses": "audit@company.com",
  "steps": [
    {
      "stepOrder": 1,
      "channel": 1,
      "templateId": "abc12345-...",
      "delayDays": 0
    }
  ]
}
```

### Retrieve campaign CC/BCC configuration

`GET /api/campaigns/{id}`

The response includes:

```json
{
  "id": "...",
  "name": "Q1 Newsletter",
  "staticCcAddresses": "manager@company.com, compliance@company.com",
  "dynamicCcField": "account_manager_email",
  "staticBccAddresses": "audit@company.com",
  ...
}
```

## Implementation Notes

### Architecture

```
Campaign (domain entity)
  ├── StaticCcAddresses  (nvarchar(2000))
  ├── DynamicCcField     (nvarchar(200))
  └── StaticBccAddresses (nvarchar(2000))

IEmailValidationService / EmailValidationService
  └── Validates individual addresses using MimeKit MailboxAddress.TryParse

ICcResolutionService / CcResolutionService
  └── Combines static + dynamic, validates, deduplicates, caps at 10

ProcessChunkJob
  └── Calls CcResolutionService per recipient, populates DispatchRequest.CcAddresses / BccAddresses

EmailDispatcher (existing, US-019)
  └── Reads CcAddresses and BccAddresses from DispatchRequest, adds to MimeMessage
```

### Key Services

| Service | Location | Lifetime | Purpose |
|---------|----------|---------|---------|
| `IEmailValidationService` | `Infrastructure/Dispatch/` | Scoped | Validates email addresses |
| `ICcResolutionService` | `Infrastructure/Dispatch/` | Scoped | Combines, validates, deduplicates CC/BCC |

### Database

The CC/BCC fields were added to the `Campaigns` table in the EF Core configuration (`CampaignConfiguration.cs`). No migration is required if the columns were already added in a prior migration; otherwise run:

```bash
dotnet ef migrations add AddCampaignCcBccFields \
  --project src/CampaignEngine.Infrastructure \
  --startup-project src/CampaignEngine.Infrastructure
dotnet ef database update \
  --project src/CampaignEngine.Infrastructure \
  --startup-project src/CampaignEngine.Infrastructure
```

## Monitoring

Invalid CC/BCC addresses are logged at Warning level with the context label. Search logs with:

```
"Invalid email address in StaticCC"
"Invalid email address in DynamicCC"
"CC recipient count * exceeds maximum"
```

These warnings indicate configuration issues that should be reviewed and corrected in the campaign settings.
