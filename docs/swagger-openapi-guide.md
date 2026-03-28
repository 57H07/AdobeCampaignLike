# Swagger / OpenAPI Documentation Guide

## Overview

CampaignEngine exposes an auto-generated **OpenAPI 3.0** specification describing all REST API
endpoints, request/response schemas, and authentication requirements.

The specification is generated at runtime by **Swashbuckle.AspNetCore 6.9** using XML documentation
comments embedded in the Web and Application assemblies.

---

## Accessing the Documentation

### Swagger UI

The interactive Swagger UI is available at:

```
https://<host>/swagger
```

The UI is enabled in **Development** and **Staging** environments only (business rule BR-1).
It is disabled in Production to prevent information disclosure.

### Raw OpenAPI JSON Spec

The machine-readable spec is served at:

```
https://<host>/swagger/v1/swagger.json
```

This endpoint is available in **all environments** so CI/CD tooling and code generators can consume
it without environment restrictions.

---

## Authentication

All API endpoints require an `X-Api-Key` header.

### How to authenticate in Swagger UI

1. Open `https://<host>/swagger`
2. Click the **Authorize** button (lock icon) at the top right
3. Enter your API key in the **ApiKey (apiKey)** field:
   ```
   ce_<your-key-value>
   ```
4. Click **Authorize** then **Close**
5. All subsequent requests from the UI will include the `X-Api-Key` header automatically

The authorization state is persisted across page refreshes (`persistAuthorization: true`).

### Obtaining an API key

Only users with the **Admin** role can create API keys. Use the management UI at
`/Admin/ApiKeys` or call the REST endpoint directly:

```http
POST /api/apikeys
X-Api-Key: <existing-admin-key>
Content-Type: application/json

{
  "name": "My integration key",
  "description": "Used by OrderService to send transactional emails",
  "rateLimitPerMinute": 500,
  "expiresInDays": 365
}
```

The response includes the plaintext key value — **copy it immediately**, it cannot be retrieved again.

---

## API Key header format

```
X-Api-Key: ce_abcdef1234567890abcdef1234567890abcdef12
```

The key must include the `ce_` prefix. Keys without the prefix will be rejected with HTTP 401.

---

## Available Endpoints

### Generic Send API

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/send` | Send a single transactional message |

### API Key Management (Admin only)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/apikeys` | List all API keys |
| GET | `/api/apikeys/{id}` | Get API key by ID |
| POST | `/api/apikeys` | Create a new API key |
| POST | `/api/apikeys/{id}/revoke` | Revoke an API key |
| POST | `/api/apikeys/{id}/rotate` | Rotate (replace) an API key |

### Templates

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/templates` | List templates (paginated) |
| GET | `/api/templates/{id}` | Get template by ID |
| POST | `/api/templates` | Create a template |
| PUT | `/api/templates/{id}` | Update a template |
| DELETE | `/api/templates/{id}` | Soft-delete a template |
| POST | `/api/templates/{id}/publish` | Publish a template |
| POST | `/api/templates/{id}/archive` | Archive a template |
| GET | `/api/templates/{id}/history` | Version history |
| GET/POST/PUT/DELETE | `/api/templates/{id}/placeholders` | Manage placeholder manifest |

### Campaigns

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/campaigns` | List campaigns (paginated) |
| GET | `/api/campaigns/{id}` | Get campaign by ID |
| POST | `/api/campaigns` | Create a campaign |
| POST | `/api/campaigns/{id}/schedule` | Schedule a campaign |
| GET | `/api/campaigns/{id}/status` | Live status with progress counters |
| POST | `/api/campaigns/estimate-recipients` | Estimate recipient count |
| GET/POST/DELETE | `/api/campaigns/{id}/attachments` | Manage attachments |

### Send Logs

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/sendlogs` | Query send log (paginated) |
| GET | `/api/sendlogs/{id}` | Get single send log entry |

### Data Sources (Admin)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/datasources` | List data sources |
| GET | `/api/datasources/{id}` | Get data source by ID |
| POST | `/api/datasources` | Create a data source |
| PUT | `/api/datasources/{id}` | Update a data source |
| PUT | `/api/datasources/{id}/schema` | Replace field schema |
| POST | `/api/datasources/{id}/test-connection` | Test connectivity |
| POST | `/api/datasources/{id}/preview` | Preview rows |

### Monitoring (Admin)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/admin/rate-limit-metrics` | Channel rate limit metrics |
| POST | `/api/admin/rate-limit-metrics/reset` | Reset metrics counters |

---

## Sending a transactional message — quick start

```http
POST /api/send
X-Api-Key: ce_<your-key>
Content-Type: application/json

{
  "templateId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "channel": 1,
  "data": {
    "firstName": "Marie",
    "lastName": "Dupont",
    "orderId": "ORD-20240328-001"
  },
  "recipient": {
    "email": "marie.dupont@example.com",
    "displayName": "Marie Dupont",
    "externalRef": "CRM-12345"
  }
}
```

**Channel values:** `1` = Email, `2` = Letter, `3` = Sms

**Successful response (HTTP 200):**

```json
{
  "trackingId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "success": true,
  "status": 2,
  "channel": 1,
  "sentAt": "2026-03-28T10:30:00Z",
  "messageId": "<20260328103000.7c9e@smtp.campaignengine.local>",
  "errorDetail": null
}
```

Use the `trackingId` to query `GET /api/sendlogs/{trackingId}` for delivery status.

---

## Rate Limiting

Each API key has a per-minute request limit (default: 1000 req/min). Exceeding the limit
returns **HTTP 429 Too Many Requests** with a `Retry-After` header indicating when the
next request can be made.

Administrators can configure per-key limits when creating or rotating keys via
the `rateLimitPerMinute` field.

---

## Error Response Format

All error responses follow the RFC 7807 Problem Details format:

```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "recipient.email": ["A valid email address is required for Email channel."]
  }
}
```

| HTTP Status | Meaning |
|-------------|---------|
| 400 | Validation error — check `errors` field |
| 401 | Missing or invalid `X-Api-Key` header |
| 403 | Authenticated but insufficient role |
| 404 | Resource not found |
| 422 | Domain rule violation (e.g. template not published) |
| 429 | Rate limit exceeded — check `Retry-After` header |
| 500 | Internal server error |

---

## Downloading the spec for code generation

The raw OpenAPI JSON can be downloaded and used with tools such as:

- **NSwag** — generate C# or TypeScript clients
- **OpenAPI Generator** — generate clients in many languages
- **Postman** — import the spec as a collection

```bash
curl -o campaignengine-api.json https://<host>/swagger/v1/swagger.json
```

To generate a C# client with NSwag:

```bash
nswag openapi2csclient /input:campaignengine-api.json /output:CampaignEngineClient.cs /namespace:CampaignEngine.Client
```

---

## Environment Configuration

Swagger behavior is controlled by environment:

| Setting | Development | Staging | Production |
|---------|-------------|---------|-----------|
| Swagger UI (`/swagger`) | Enabled | Enabled | Disabled |
| Raw spec (`/swagger/v1/swagger.json`) | Available | Available | Available |

To run the application in Development mode locally:

```bash
dotnet run --project src/CampaignEngine.Web --environment Development
```

---

## Implementation Details

- **Package:** `Swashbuckle.AspNetCore` v6.9.0
- **Configuration:** `src/CampaignEngine.Web/OpenApi/SwaggerServiceExtensions.cs`
- **Custom theme CSS:** `src/CampaignEngine.Web/wwwroot/swagger-ui/custom.css`
- **Request/response examples:** `src/CampaignEngine.Web/OpenApi/Examples/`
- **Spec validity tests:** `tests/CampaignEngine.Infrastructure.Tests/OpenApi/OpenApiSpecValidityTests.cs`

XML documentation is generated from:
- `src/CampaignEngine.Web` — controller action summaries
- `src/CampaignEngine.Application` — DTO descriptions and property summaries
