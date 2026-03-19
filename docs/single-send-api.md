# Single Send API — POST /api/send

## Overview

The Generic Send API provides a simple, synchronous REST endpoint for integration consumers to trigger
transactional messages without interacting with the Campaign Orchestrator.

**Use cases:**
- Sending a welcome email when a user registers
- Sending an OTP SMS on login
- Triggering a PDF letter confirmation after contract signature

---

## Endpoint

```
POST /api/send
Content-Type: application/json
```

---

## Request Body

```json
{
  "templateId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "channel": "Email",
  "data": {
    "firstName": "Alice",
    "orderNumber": "ORD-2026-001",
    "totalAmount": "149.99"
  },
  "recipient": {
    "email": "alice@example.com",
    "displayName": "Alice Martin",
    "externalRef": "USR-12345"
  }
}
```

### Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `templateId` | GUID | Yes | ID of a Published template |
| `channel` | string | Yes | `"Email"`, `"Sms"`, or `"Letter"` |
| `data` | object | Yes | Key-value pairs for template placeholder substitution |
| `recipient.email` | string | Email channel | Recipient email address (validated format) |
| `recipient.phoneNumber` | string | SMS channel | E.164 format, e.g. `+33612345678` |
| `recipient.displayName` | string | No | Display name (cosmetic) |
| `recipient.externalRef` | string | No | Caller system reference for log correlation |

---

## Response — 200 OK

```json
{
  "trackingId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "success": true,
  "status": "Sent",
  "channel": "Email",
  "sentAt": "2026-03-19T14:23:00Z",
  "messageId": "smtp-msg-abc123",
  "errorDetail": null
}
```

| Field | Type | Description |
|-------|------|-------------|
| `trackingId` | GUID | Unique ID for this send. Use to query the send log. |
| `success` | bool | `true` if dispatched successfully |
| `status` | string | `"Sent"` or `"Failed"` |
| `channel` | string | The channel used |
| `sentAt` | datetime | UTC timestamp of dispatch |
| `messageId` | string or null | Provider message ID (SMTP message-id, SMS provider ID) |
| `errorDetail` | string or null | Error description if `success` is `false` |

---

## Error Responses

### 400 Bad Request — Validation failure

Returned when business rules are violated.

```json
{
  "status": 400,
  "title": "Validation Error",
  "detail": "One or more validation errors occurred.",
  "traceId": "00-abc123-01"
}
```

**Common causes:**

| Cause | Error message |
|-------|---------------|
| Template not Published | `Template 'X' is not Published (current status: Draft).` |
| Channel mismatch | `Channel mismatch: request specifies 'Sms' but template is 'Email'.` |
| Missing placeholder data | `Missing required placeholder data keys: 'firstName', 'orderNumber'.` |
| Missing email (Email channel) | `Recipient email address is required for Email channel.` |
| Invalid email format | `Recipient email address 'x' is not a valid email format.` |
| Missing phone (SMS channel) | `Recipient phone number is required for SMS channel.` |
| Invalid phone format | `Recipient phone number 'x' must be in E.164 format (e.g. +33612345678).` |

### 404 Not Found — Template does not exist

```json
{
  "status": 404,
  "title": "Resource Not Found",
  "detail": "Entity 'Template' with key '3fa85f64-...' was not found.",
  "traceId": "00-abc123-01"
}
```

---

## Business Rules

1. **Template must be Published** — Draft and Archived templates are rejected with 400.
2. **Channel must match template** — The `channel` field in the request must equal the template's channel.
3. **All placeholder keys required** — Every key declared in the template's placeholder manifest must appear in `data`.
4. **Recipient validation** — Email address required and validated for Email channel; E.164 phone number required for SMS.
5. **Response time target** — < 500ms at p95 (synchronous, no queue).
6. **Tracking ID** — Every response (success or failure) includes a `trackingId` that maps to a `SendLog` entry.

---

## Tracking and Audit

Every call to `POST /api/send` creates a `SendLog` entry recording:
- Status: `Pending` → `Sent` or `Failed`
- Recipient address (masked in logs)
- Correlation to `trackingId`
- Error detail if dispatch failed

Query the send log via `GET /api/sendlogs?correlationId={trackingId}` (requires US-034).

---

## Swagger / OpenAPI

The endpoint is documented in the interactive Swagger UI available at `/swagger` in non-production environments.

The OpenAPI specification is available at `/swagger/v1/swagger.json`.
