# Attachment Management Guide

**Feature:** US-028 — Static and Dynamic Attachment Management
**Module:** Campaign Orchestrator

## Overview

CampaignEngine supports two attachment strategies for campaign sends:

| Strategy | Description | When resolved |
|----------|-------------|---------------|
| **Static** | Operator uploads a file; all recipients receive the same file | At campaign creation |
| **Dynamic** | A data source field specifies a per-recipient file path | At send time, per recipient |

---

## Business Rules

1. **Extension whitelist:** `.pdf`, `.docx`, `.xlsx`, `.png`, `.jpg`, `.jpeg`
2. **Per-file size limit:** 10 MB maximum
3. **Total size limit per send:** 25 MB across all attachments
4. **Static attachments:** stored on file share at campaign creation; all recipients receive the same file(s)
5. **Dynamic attachments:** file path read from a data source field at send time, per recipient
6. **Missing dynamic files:** logged as a warning — the send proceeds without the attachment (never blocks delivery)

---

## File Storage

Attachment files are stored at a configurable base path (UNC share or local directory).

### Configuration

```json
{
  "CampaignEngine": {
    "Attachments": {
      "Storage": {
        "BasePath": "\\\\fileserver\\shares\\campaign-attachments",
        "AllowedExtensions": [".pdf", ".docx", ".xlsx", ".png", ".jpg", ".jpeg"],
        "MaxFileSizeBytes": 10485760,
        "MaxTotalSizeBytes": 26214400
      }
    }
  }
}
```

**Q7 note:** For production, replace `BasePath` with a UNC path accessible by the application service account. Both UNC paths (`\\server\share`) and local absolute paths are supported. The application must have read/write permissions on the base path.

### Storage Layout

Files are stored in campaign-scoped subdirectories with a GUID prefix to prevent name collisions:

```
{BasePath}/
  {campaignId}/
    {guid}_{originalFileName}
```

Example:
```
\\fileserver\shares\campaign-attachments\
  a1b2c3d4-....\
    3f8a1c2b..._invoice_template.pdf
    9d7e4f1a..._terms_conditions.pdf
```

---

## API Reference

### List Attachments

```http
GET /api/campaigns/{id}/attachments
Authorization: X-Api-Key <key>
```

Returns all attachments (static and dynamic) for the campaign.

**Response 200:**
```json
[
  {
    "id": "...",
    "campaignId": "...",
    "fileName": "invoice_template.pdf",
    "filePath": "\\\\server\\share\\campaign-id\\guid_invoice_template.pdf",
    "fileSizeBytes": 524288,
    "contentType": "application/pdf",
    "isDynamic": false,
    "dynamicFieldName": null,
    "createdAt": "2026-03-28T10:00:00Z"
  },
  {
    "id": "...",
    "campaignId": "...",
    "fileName": "[dynamic:personal_doc_path]",
    "filePath": "",
    "fileSizeBytes": 0,
    "contentType": "",
    "isDynamic": true,
    "dynamicFieldName": "personal_doc_path",
    "createdAt": "2026-03-28T10:05:00Z"
  }
]
```

### Upload Static Attachment

```http
POST /api/campaigns/{id}/attachments
Authorization: X-Api-Key <key>
Content-Type: multipart/form-data
```

Upload a file that will be sent to all recipients.

**Form field:** `file` (required)

**Response 201:** `CampaignAttachmentDto`

**Error 400 (validation failure):**
```json
{
  "errors": {
    "file": ["File extension '.exe' is not allowed. Allowed extensions: .pdf, .docx, ..."]
  }
}
```

### Register Dynamic Attachment Field

```http
POST /api/campaigns/{id}/attachments/dynamic
Authorization: X-Api-Key <key>
Content-Type: application/json
```

```json
{
  "dynamicFieldName": "personal_doc_path"
}
```

Registers a data source field whose value will be treated as a per-recipient file path at send time.

**Response 201:** `CampaignAttachmentDto`

### Delete Attachment

```http
DELETE /api/campaigns/{id}/attachments/{attachmentId}
Authorization: X-Api-Key <key>
```

Removes the attachment record. For static attachments, the file is also deleted from the file share.

**Response 204 No Content**

---

## Operator UI (Campaign Detail Page)

### Static Attachment Uploader

On the Campaign Detail page (Draft status only), the **Attachments** panel allows operators to:

1. Click **Choose File** and select a PDF, DOCX, XLSX, PNG, or JPG file
2. Click **Upload** to store the file and register the attachment

The panel lists all uploaded attachments with their name, MIME type, and size. Attachments can be removed using the trash icon.

### Dynamic Attachment Field Mapper

In the same Attachments panel, operators can also configure a dynamic attachment:

1. Enter the **data source field name** that holds the per-recipient file path
2. Click **Map Field** to register the mapping

At send time, CampaignEngine reads the field value from each recipient's data row and attempts to load the file from that path. If the file is missing or the field is empty, a warning is logged and the recipient receives the email without that attachment.

---

## Implementation Details

### Validation Flow (Static Attachments)

```
Upload request
  → AttachmentValidationService.ValidateFile(fileName, sizeBytes)
      → Extension whitelist check (BR-1)
      → Per-file size check: ≤ 10 MB (BR-2)
  → AttachmentRepository.GetTotalFileSizeByCampaignAsync()
  → AttachmentValidationService.ValidateTotalSize(existingTotal + newSize)
      → Total size check: ≤ 25 MB (BR-3)
  → FileUploadService.UploadAsync()
      → Directory.CreateDirectory({BasePath}/{campaignId})
      → File.WriteAllBytesAsync({guid}_{originalFileName})
  → AttachmentRepository.AddAsync()
  → UnitOfWork.CommitAsync()
```

### Dynamic Resolution Flow (At Send Time)

```
DynamicAttachmentResolver.Resolve(staticAttachments, dynamicFieldName, recipientData)
  → Return static attachments list
  → If dynamicFieldName is null/empty: return static only
  → recipientData.TryGetValue(dynamicFieldName)
      → Not found: log warning, return static only (BR-5)
      → Value null/empty: log warning, return static only (BR-5)
  → File.Exists(filePath)
      → False: log warning, return static only (BR-5)
      → True: AttachmentInfo.FromFilePath(filePath) → add to result
  → Return static + dynamic AttachmentInfo list
```

### Key Classes

| Class | Location | Responsibility |
|-------|----------|----------------|
| `CampaignAttachment` | `Domain/Entities/` | Attachment entity (static + dynamic) |
| `IAttachmentValidationService` | `Application/Interfaces/` | Validates type and size |
| `AttachmentValidationService` | `Infrastructure/Attachments/` | Enforces whitelist + size limits |
| `IFileUploadService` | `Application/Interfaces/` | Writes files to file share |
| `FileUploadService` | `Infrastructure/Attachments/` | UNC/local path storage implementation |
| `IDynamicAttachmentResolver` | `Application/Interfaces/` | Resolves per-recipient file paths |
| `DynamicAttachmentResolver` | `Infrastructure/Attachments/` | Field lookup + file existence check |
| `IAttachmentService` | `Application/Interfaces/` | Orchestrates upload + DB persistence |
| `AttachmentService` | `Infrastructure/Attachments/` | Coordinates validation, upload, DB |
| `AttachmentStorageOptions` | `Infrastructure/Configuration/` | Binds `CampaignEngine:Attachments:Storage` |

---

## Security Considerations

- **Extension whitelist** prevents executables and scripts from being uploaded or sent as attachments.
- **Size limits** prevent denial-of-service via oversized files.
- **GUID prefix** on stored file names prevents path traversal and accidental overwrites.
- **Dynamic paths** are read from your own controlled data source; ensure data source records are trustworthy before using the dynamic attachment feature.
- **Operator role required** for upload and registration (`RequireOperatorOrAdmin` authorization policy).

---

## Troubleshooting

### Missing dynamic attachment warning in logs

```
[WARN] Dynamic attachment file not found at path '\\server\share\recipient123.pdf' (field 'personal_doc_path'). Sending without dynamic attachment.
```

**Cause:** The file path stored in the recipient's data source record does not exist on the configured file share.

**Resolution:** Ensure:
1. The file share path is accessible from the CampaignEngine application server.
2. Recipient data paths use the same UNC convention as the server.
3. Files are pre-populated before the campaign runs.

### Upload fails with "File extension not allowed"

Only `.pdf`, `.docx`, `.xlsx`, `.png`, `.jpg`, `.jpeg` files are supported. Convert the file to a supported format before uploading.

### Upload fails with "too large"

Individual files must not exceed 10 MB. Compress or split the document before uploading. The combined total of all static attachments per campaign must not exceed 25 MB.

### File share permissions error

The application service account must have **read + write** access to the configured `BasePath`. On Windows/IIS, grant permissions to the application pool identity or the service account running the application.
