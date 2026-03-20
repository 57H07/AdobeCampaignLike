# SMTP Configuration Guide

**Related US:** US-019 - Email Dispatcher (SMTP)
**TASK:** TASK-019-08

---

## Overview

CampaignEngine sends HTML emails via a configurable SMTP server using [MailKit](https://github.com/jstedfast/MailKit).
All SMTP settings are declared in `appsettings.json` under the `"Smtp"` section and bound to `SmtpOptions`.

---

## Configuration Reference

```json
{
  "Smtp": {
    "Host": "smtp.yourprovider.com",
    "Port": 587,
    "UseSsl": true,
    "UserName": "your-smtp-username",
    "Password": "your-smtp-password",
    "FromAddress": "noreply@yourcompany.com",
    "FromName": "Your Company Name",
    "ReplyToAddress": "",
    "TimeoutSeconds": 30,
    "MaxConnectRetries": 3,
    "AllowedAttachmentExtensions": [ ".pdf", ".docx", ".xlsx", ".png", ".jpg", ".jpeg" ],
    "MaxAttachmentFileSizeBytes": 10485760,
    "MaxAttachmentTotalSizeBytes": 26214400
  }
}
```

### Field Descriptions

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Host` | string | `""` | SMTP server hostname or IP address |
| `Port` | int | `587` | SMTP port. Use 587 (STARTTLS), 465 (SMTPS), or 25 (plain) |
| `UseSsl` | bool | `true` | Enables STARTTLS when `true`. Disable only in dev/test environments |
| `UserName` | string | `""` | SMTP authentication username. Leave empty to skip authentication |
| `Password` | string | `""` | SMTP authentication password |
| `FromAddress` | string | `""` | **Required.** Sender email address. Configurable per environment (Business Rule 1) |
| `FromName` | string | `"CampaignEngine"` | Display name shown in the "From" header |
| `ReplyToAddress` | string | `null` | Default Reply-To address. Optional, can be overridden per campaign (Business Rule 2) |
| `TimeoutSeconds` | int | `30` | Connection and send timeout in seconds |
| `MaxConnectRetries` | int | `3` | Maximum connection retry attempts |
| `AllowedAttachmentExtensions` | string[] | `[".pdf",".docx",".xlsx",".png",".jpg",".jpeg"]` | Attachment type whitelist (Business Rule 3) |
| `MaxAttachmentFileSizeBytes` | long | `10485760` (10 MB) | Maximum size per attachment file (Business Rule 4) |
| `MaxAttachmentTotalSizeBytes` | long | `26214400` (25 MB) | Maximum total attachment size per email (Business Rule 4) |

---

## Business Rules

### Rule 1 — From Address Per Environment

Set different `FromAddress` values per environment using appsettings overrides:

- `appsettings.Development.json`: `"FromAddress": "dev@yourcompany.com"`
- `appsettings.Production.json`: `"FromAddress": "noreply@yourcompany.com"`

### Rule 2 — Optional Reply-To Per Campaign

The `ReplyToAddress` in `appsettings.json` is the system default. A campaign can override it
by setting `DispatchRequest.ReplyToAddress` at send time. If neither is set, no Reply-To header
is added to the email.

### Rule 3 — Attachment Whitelist

Only files with these extensions are permitted: `.pdf`, `.docx`, `.xlsx`, `.png`, `.jpg`, `.jpeg`.

If an attachment has a disallowed extension, the send fails permanently (not retried).
The error is recorded in the SEND_LOG with details of the blocked file.

To add new types, update `AllowedAttachmentExtensions` in `appsettings.json`.

### Rule 4 — Attachment Size Limits

- **Per file**: Maximum 10 MB (configurable via `MaxAttachmentFileSizeBytes`).
- **Total**: Maximum 25 MB across all attachments (configurable via `MaxAttachmentTotalSizeBytes`).

Violations fail permanently and are logged. They are not retried.

---

## Port and TLS Reference

| Port | Protocol | When to Use |
|------|----------|-------------|
| 587  | STARTTLS | Recommended for most providers |
| 465  | SMTPS (implicit TLS) | Some older providers |
| 25   | Plain SMTP | Internal relay servers only |

For port 465 (implicit SSL), set `"UseSsl": true` and `"Port": 465`. MailKit will
use `SecureSocketOptions.SslOnConnect` automatically when connecting to port 465.

For port 587 (STARTTLS), set `"UseSsl": true` and `"Port": 587`. MailKit will negotiate
STARTTLS after connecting.

For local SMTP relay (no TLS), set `"UseSsl": false` and leave `UserName` empty.

---

## SMTP Error Handling

### Transient Errors (Auto-Retried)

These errors are considered temporary. The dispatcher returns `IsTransientFailure = true`
and the retry mechanism (US-035) will re-attempt the send:

| SMTP Code | Meaning |
|-----------|---------|
| 4xx | All 4xx responses (temporary failures) |
| 421 | Service not available |
| 450 | Mailbox temporarily unavailable |
| 451 | Local error, try again |
| 452 | Insufficient storage |
| Socket timeout | Network unreachable, connection refused |
| Protocol error | Connection dropped mid-transaction |

### Permanent Errors (Not Retried)

These errors indicate the send cannot succeed without intervention:

| SMTP Code | Meaning |
|-----------|---------|
| 5xx | All 5xx responses (permanent failures) |
| 550 | Recipient not found |
| 551 | User not local |
| 554 | Transaction failed (message rejected) |
| Authentication | Wrong credentials |

All errors — whether transient or permanent — are recorded in the SEND_LOG table with
`ErrorDetail` set to the SMTP response text. See [send-log-schema.md](./send-log-schema.md).

---

## Attachments From File Paths

Attachments can be specified as in-memory byte arrays or as file paths on disk.
Use `AttachmentInfo.FromFilePath(path)` to create a path-based attachment:

```csharp
var request = new DispatchRequest
{
    Recipient = new RecipientInfo { Email = "user@example.com" },
    Content = "<p>Please find the attached document.</p>",
    Subject = "Your Document",
    Attachments =
    [
        AttachmentInfo.FromFilePath("/share/docs/invoice_12345.pdf"),
        AttachmentInfo.FromBytes("summary.png", imageBytes, "image/png")
    ]
};
```

When `FilePath` is set, the file is read from disk at send time. If the file is not found,
the send fails with a permanent error (file not found is not retried).

---

## CC and BCC Support

CC and BCC recipient lists are set on `DispatchRequest`:

```csharp
var request = new DispatchRequest
{
    Recipient = new RecipientInfo { Email = "customer@example.com" },
    CcAddresses = ["manager@example.com", "supervisor@example.com"],
    BccAddresses = ["audit@example.com"],
    ReplyToAddress = "support@example.com"  // overrides SmtpOptions default
};
```

Invalid email addresses in CC or BCC lists are logged and silently skipped.
The primary recipient's address is always validated — an empty or null primary
address fails immediately with a permanent error.

---

## Environment-Specific Configurations

### Development / Test

Use [Mailpit](https://github.com/axllent/mailpit) or [smtp4dev](https://github.com/rnwood/smtp4dev)
as a local SMTP server that captures all outgoing emails without delivering them:

```json
{
  "Smtp": {
    "Host": "localhost",
    "Port": 1025,
    "UseSsl": false,
    "UserName": "",
    "Password": "",
    "FromAddress": "dev@localhost",
    "FromName": "CampaignEngine Dev"
  }
}
```

### Production

Store credentials securely using ASP.NET Core Data Protection or environment variables.
Never commit passwords to source control:

```json
{
  "Smtp": {
    "Host": "smtp.sendgrid.net",
    "Port": 587,
    "UseSsl": true,
    "UserName": "apikey",
    "Password": "<from-environment-variable-or-key-vault>",
    "FromAddress": "noreply@yourcompany.com",
    "FromName": "Your Company"
  }
}
```

---

## Open Question Q3 — SMTP Throttling

SMTP server limits (messages/second) affect the throttling configuration in US-022.
Until Q3 is resolved, the default throttle is 100 emails/second (configured in
`CampaignEngine.Dispatch.Email.ThrottlePerSecond` in `appsettings.json`).

If your SMTP server has a lower limit (e.g. 50/sec), update:

```json
{
  "CampaignEngine": {
    "Dispatch": {
      "Email": {
        "ThrottlePerSecond": 50
      }
    }
  }
}
```

---

## See Also

- [channel-dispatcher-extension-guide.md](./channel-dispatcher-extension-guide.md) — Adding new channel dispatchers
- [send-log-schema.md](./send-log-schema.md) — SEND_LOG schema and status codes
- [channel-post-processing.md](./channel-post-processing.md) — CSS inlining for email (PreMailer.Net)
