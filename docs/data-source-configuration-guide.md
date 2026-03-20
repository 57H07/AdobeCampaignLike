# Data Source Configuration Guide

**Epic:** Data Source Connector (US-014)
**Roles required:** Admin (create/edit), Operator (read-only)

---

## Overview

Data sources represent external repositories from which CampaignEngine fetches recipient data.
They are declared once by an IT Administrator and then consumed by Campaign Operators when
configuring targeting for campaigns and templates.

Supported types in Phase 1:

| Type | Value | Use case |
|------|-------|----------|
| SQL Server | `1` | Enterprise databases (CRM, ERP, custom tables) |
| REST API | `2` | External HTTP endpoints returning JSON |

---

## Connection String Encryption

All connection strings are **encrypted at rest** using ASP.NET Core Data Protection.

- The plaintext string is never stored in the database.
- Data Protection keys are managed automatically (file system on IIS, Azure Key Vault in cloud).
- The UI uses a password-type input to prevent shoulder surfing.
- The API never returns the connection string; only `hasConnectionString: true/false` is exposed.

When you update a data source and leave the connection string field blank, the existing
encrypted value is preserved.

---

## SQL Server Connection Strings

The SQL Server connector uses `System.Data.SqlClient` and supports all standard ADO.NET
connection string keywords.

### Windows Authentication

```
Server=myserver\SQLEXPRESS;Database=CampaignData;Trusted_Connection=True;
```

### SQL Authentication

```
Server=myserver;Database=CampaignData;User Id=campaign_ro;Password=MyStr0ngPass!;
```

### Recommended settings for production

```
Server=myserver;Database=CampaignData;User Id=campaign_ro;Password=MyStr0ngPass!;
Connect Timeout=30;TrustServerCertificate=False;Encrypt=True;
Application Name=CampaignEngine;
```

**Security note:** The database account used should have **read-only** SELECT permission
on the target tables. Write access is never required by CampaignEngine connectors.

---

## REST API Connection Strings

For REST API data sources, the "connection string" field is the **base URL** of the API.

```
https://api.example.com/v1
```

The connection test issues a HEAD request to the base URL to verify reachability. A 2xx,
3xx, 4xx response all indicate the endpoint is reachable (even 401 Unauthorized is a
success — it means the server responded).

Authentication configuration (API key, OAuth2) is managed separately in US-017.

---

## Field Schema Definition

Each data source can have a declared schema listing the available fields.
This schema serves two purposes:

1. **Filter expressions** — only filterable fields can be used in campaign audience filters.
2. **Recipient address mapping** — fields marked `isRecipientAddress = true` are
   recognised as email/phone fields for dispatch.

### Field data types

| Value | Description |
|-------|-------------|
| `nvarchar` | Text string |
| `int` | 32-bit integer |
| `bigint` | 64-bit integer |
| `datetime` | Date and time |
| `date` | Date only |
| `time` | Time only |
| `bit` | Boolean |
| `decimal` | Decimal number |
| `float` | Floating-point number |
| `uniqueidentifier` | GUID / UUID |

### Auto-discovery vs manual definition

**Manual definition** is available via the Edit Schema page or the
`PUT /api/datasources/{id}/schema` endpoint.

**Auto-discovery** from SQL Server `INFORMATION_SCHEMA` is implemented in US-015
(SQL Server Connector). Once the connector is active, a "Discover Schema" button
will be available on the Details page.

---

## REST API Reference

### Endpoints

| Method | Path | Description | Role |
|--------|------|-------------|------|
| `GET` | `/api/datasources` | List data sources (paginated) | Operator, Admin |
| `GET` | `/api/datasources/{id}` | Get data source by ID | Operator, Admin |
| `POST` | `/api/datasources` | Create data source | Admin |
| `PUT` | `/api/datasources/{id}` | Update data source | Admin |
| `PUT` | `/api/datasources/{id}/schema` | Replace field schema | Admin |
| `PATCH` | `/api/datasources/{id}/active?isActive=true` | Activate/deactivate | Admin |
| `POST` | `/api/datasources/{id}/test-connection` | Test existing connection | Admin |
| `POST` | `/api/datasources/test-connection` | Test raw connection string | Admin |

### Create data source — example

```http
POST /api/datasources
Content-Type: application/json
Authorization: Bearer <token>

{
  "name": "Customer CRM",
  "type": 1,
  "connectionString": "Server=crm-db;Database=crm;User Id=reader;Password=secret;",
  "description": "Main CRM customer table",
  "fields": [
    { "fieldName": "CustomerId",  "dataType": "int",     "isFilterable": true,  "isRecipientAddress": false },
    { "fieldName": "Email",       "dataType": "nvarchar", "isFilterable": true,  "isRecipientAddress": true },
    { "fieldName": "Segment",     "dataType": "nvarchar", "isFilterable": true,  "isRecipientAddress": false }
  ]
}
```

Response `201 Created`:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Customer CRM",
  "type": 1,
  "typeName": "SqlServer",
  "description": "Main CRM customer table",
  "isActive": true,
  "hasConnectionString": true,
  "fields": [ ... ],
  "createdAt": "2026-03-20T10:00:00Z",
  "updatedAt": "2026-03-20T10:00:00Z"
}
```

### Test connection — example

```http
POST /api/datasources/test-connection
Content-Type: application/json
Authorization: Bearer <token>

{
  "type": 1,
  "connectionString": "Server=crm-db;Database=crm;User Id=reader;Password=secret;"
}
```

Response `200 OK`:

```json
{
  "success": true,
  "message": "Connected successfully to SQL Server in 42ms.",
  "elapsedMs": 42,
  "testedAt": "2026-03-20T10:01:00Z"
}
```

---

## UI Walkthrough

### Creating a data source

1. Navigate to **Admin > Data Sources**.
2. Click **New Data Source**.
3. Enter the **Name**, **Type**, and **Connection String**.
   - The connection string input is masked (password field). Use the eye button to reveal.
   - Click **Test Connection** to verify before saving.
4. Optionally add field schema rows.
5. Click **Create Data Source**.

### Editing the field schema

1. From the data source **Details** page, click **Edit Schema**.
2. Add, remove, or modify field rows.
3. Click **Save Schema**.

   > Warning: saving the schema **replaces all existing fields**. Campaigns using filters
   > on removed fields may need to be updated.

### Activating / Deactivating

Use the play/pause button in the data source list or the API endpoint to toggle the
active status. Inactive data sources cannot be selected for new campaigns.

---

## Business Rules

1. **Name uniqueness** — data source names must be unique across all types.
2. **Encryption at rest** — connection strings are always encrypted using Data Protection.
3. **Admin-only writes** — only Admin role can create, update, or delete data sources.
   Operator role has read access only.
4. **Schema flexibility** — schema can be auto-discovered (SQL Server, via US-015) or
   manually defined at any time.
5. **Connection test** — testing a connection does not persist any data; it opens and
   immediately closes the connection.

---

## Troubleshooting

| Symptom | Likely cause | Resolution |
|---------|-------------|------------|
| "Connection failed: A network-related error..." | SQL Server hostname not reachable | Check firewall rules; verify server name |
| "Connection failed: Login failed for user..." | Wrong credentials | Verify User Id/Password in connection string |
| "Connection failed: Cannot open database..." | Database does not exist or user lacks access | Grant SELECT permission to the user |
| REST API "not a valid absolute URL" | Missing `https://` prefix | Add the scheme to the base URL |
| REST API "HTTP 500" | Remote server error | Check the API server logs |
| Test passes but campaign fails | Schema missing recipient address field | Edit Schema, mark the email/phone field as `isRecipientAddress = true` |
