# Letter Template Upload API

**US-011 | F-205 — Multipart upload API for Letter templates**

---

## Overview

Two dedicated endpoints allow API integrators (and automation pipelines) to manage Letter channel
templates by uploading DOCX files via `multipart/form-data`. These endpoints complement the generic
template JSON endpoints and are the only way to associate a DOCX binary with a Letter template.

**Authorization:** Both endpoints require the `Designer` or `Admin` role. The `CampaignManager` role
is explicitly excluded.

---

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/templates/letter` | Create a new Letter template (DOCX upload) |
| `PUT`  | `/api/templates/{id}/letter` | Update an existing Letter template (DOCX optional) |

---

## POST /api/templates/letter

Creates a new Letter template from a DOCX file upload. The template is created in `Draft` status.

### Request

```
POST /api/templates/letter
Content-Type: multipart/form-data
X-Api-Key: <your-api-key>
```

| Part | Type | Required | Constraints | Description |
|------|------|----------|-------------|-------------|
| `name` | string | Yes | max 200 chars | Unique display name within the Letter channel |
| `file` | binary | Yes | max 10 MB | The DOCX template file |
| `description` | string | No | max 500 chars | Optional human-readable description |

### cURL example

```bash
curl -X POST https://campaignengine.example.com/api/templates/letter \
  -H "X-Api-Key: your-api-key-here" \
  -F "name=Q4 Invoice Letter" \
  -F "description=Standard invoice template for Q4 campaign" \
  -F "file=@/path/to/invoice-template.docx"
```

### Success response — HTTP 201

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Q4 Invoice Letter",
  "channel": "Letter",
  "bodyPath": "templates/3fa85f64-5717-4562-b3fc-2c963f66afa6/v1.docx",
  "bodyChecksum": null,
  "status": "Draft",
  "version": 1,
  "isSubTemplate": false,
  "description": "Standard invoice template for Q4 campaign",
  "createdAt": "2026-03-31T10:00:00Z",
  "updatedAt": "2026-03-31T10:00:00Z",
  "placeholderManifests": []
}
```

The `bodyPath` field is always a **relative path** (e.g., `templates/{id}/v1.docx`). It never contains
the server storage root or any drive-letter prefix.

### Error responses

| HTTP Status | Condition |
|-------------|-----------|
| `400 Bad Request` | Missing or invalid `name` or `file` part; validation failure |
| `401 Unauthorized` | Missing or invalid API key |
| `403 Forbidden` | Authenticated user does not have the Designer or Admin role |
| `409 Conflict` | A template with the same name already exists in the Letter channel |
| `413 Request Entity Too Large` | File exceeds the 10 MB limit |
| `422 Unprocessable Entity` | Business rule violation (e.g., DOCX structural error) |

---

## PUT /api/templates/{id}/letter

Updates an existing Letter template. The `file` part is optional — if omitted, the existing DOCX is
retained and only metadata fields (`name`, `description`) are updated.

### Request

```
PUT /api/templates/{id}/letter
Content-Type: multipart/form-data
X-Api-Key: <your-api-key>
```

| Part | Type | Required | Constraints | Description |
|------|------|----------|-------------|-------------|
| `name` | string | Yes | max 200 chars | New display name (must remain unique in Letter channel) |
| `file` | binary | No | max 10 MB | Replacement DOCX file; omit to keep existing |
| `description` | string | No | max 500 chars | Updated description; omit to clear |

### cURL example — update with new file

```bash
curl -X PUT https://campaignengine.example.com/api/templates/3fa85f64-5717-4562-b3fc-2c963f66afa6/letter \
  -H "X-Api-Key: your-api-key-here" \
  -F "name=Q4 Invoice Letter v2" \
  -F "description=Revised layout with new branding" \
  -F "file=@/path/to/invoice-template-v2.docx"
```

### cURL example — metadata-only update (no new file)

```bash
curl -X PUT https://campaignengine.example.com/api/templates/3fa85f64-5717-4562-b3fc-2c963f66afa6/letter \
  -H "X-Api-Key: your-api-key-here" \
  -F "name=Q4 Invoice Letter — Renamed"
```

### Success response — HTTP 200

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Q4 Invoice Letter v2",
  "channel": "Letter",
  "bodyPath": "templates/3fa85f64-5717-4562-b3fc-2c963f66afa6/v2.docx",
  "bodyChecksum": null,
  "status": "Draft",
  "version": 2,
  "isSubTemplate": false,
  "description": "Revised layout with new branding",
  "createdAt": "2026-03-31T10:00:00Z",
  "updatedAt": "2026-03-31T11:30:00Z",
  "placeholderManifests": []
}
```

### Error responses

| HTTP Status | Condition |
|-------------|-----------|
| `400 Bad Request` | Missing or invalid `name` part; validation failure |
| `401 Unauthorized` | Missing or invalid API key |
| `403 Forbidden` | Authenticated user does not have the Designer or Admin role |
| `404 Not Found` | Template with the specified `id` does not exist |
| `409 Conflict` | A template with the same name already exists in the Letter channel |
| `413 Request Entity Too Large` | File exceeds the 10 MB limit |
| `422 Unprocessable Entity` | Channel mismatch: the target template is not a Letter template |

---

## File storage conventions

When a DOCX file is uploaded, the CampaignEngine stores it on the server using a versioned path:

```
templates/{templateId}/v{version}.docx
```

For example:
- First upload (version 1): `templates/3fa85f64-5717-4562-b3fc-2c963f66afa6/v1.docx`
- After one update (version 2): `templates/3fa85f64-5717-4562-b3fc-2c963f66afa6/v2.docx`

Previous versions are preserved in a history sub-directory for audit purposes.
The `bodyPath` field in the response always contains this **relative path** — never an absolute
file system path or a UNC share prefix.

---

## Business rules

1. **Name uniqueness per channel**: template names must be unique within the `Letter` channel. Names
   may be reused across different channels (Email, SMS, Letter) independently.

2. **File optional on update**: when updating, omitting the `file` part retains the existing DOCX.
   Only `name` and `description` are changed in that case.

3. **CampaignManager excluded**: users with only the `CampaignManager` role cannot create or update
   templates. They can still read templates via `GET /api/templates`.

4. **Channel lock**: a Letter template cannot be changed to Email or SMS via this endpoint. Attempting
   to PUT to `/api/templates/{id}/letter` where `id` refers to a non-Letter template returns HTTP 422.

---

## Related documentation

- [API Authentication Guide](api-authentication-guide.md) — X-Api-Key setup
- [Role Permissions Matrix](role-permissions-matrix.md) — Designer, Admin, CampaignManager roles
- [Template Lifecycle Guide](template-lifecycle-guide.md) — Draft → Published → Archived
- [Template Versioning Guide](template-versioning-guide.md) — version history and audit trail
- [File Size Limit Enforcement](../docs/letter-channel-configuration-guide.md) — 10 MB limit (F-204)
