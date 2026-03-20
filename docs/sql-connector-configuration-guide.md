# SQL Server Connector Configuration Guide

**Epic:** Data Source Connector (US-015)
**Roles required:** Admin (configure connectors), Operator (use in campaigns)

---

## Overview

The SQL Server connector (`SqlServerConnector`) implements `IDataSourceConnector` using
**Dapper** for lightweight data access. It enables Campaign Operators to target recipient
populations stored in any SQL Server database — CRM tables, ERP data, custom marketing
databases — without writing raw SQL.

Key design decisions:

| Concern | Implementation |
|---------|----------------|
| SQL injection prevention | All filter values use Dapper `DynamicParameters` (parameterized SQL) |
| Read-only access | Only SELECT statements are ever executed |
| Query timeout | 30 seconds (hardcoded per business rule) |
| Connection pooling | ADO.NET connection pool, configurable via `SqlServerConnector` section |
| Schema discovery | Auto-discovers columns from `INFORMATION_SCHEMA.COLUMNS` |

---

## Configuration

### appsettings.json

```json
{
  "SqlServerConnector": {
    "ConnectTimeoutSeconds": 30,
    "MinPoolSize": 0,
    "MaxPoolSize": 100
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `ConnectTimeoutSeconds` | `30` | Timeout in seconds when opening a new connection to the SQL Server instance. |
| `MinPoolSize` | `0` | Minimum connections maintained in the ADO.NET connection pool. `0` means no idle connections are guaranteed. |
| `MaxPoolSize` | `100` | Maximum simultaneous connections in the pool. Increase for high-concurrency batch campaigns (e.g., 8 Hangfire workers × N parallel chunks). |

### Environment overrides

```json
// appsettings.Production.json
{
  "SqlServerConnector": {
    "ConnectTimeoutSeconds": 15,
    "MinPoolSize": 5,
    "MaxPoolSize": 200
  }
}
```

---

## Registering a SQL Server Data Source

SQL Server data sources are declared by an Admin via the UI or the REST API (US-014).
The connector is selected automatically based on `DataSourceType.SqlServer`.

### API

```http
POST /api/datasources
Content-Type: application/json

{
  "name": "Customer CRM",
  "type": 1,
  "connectionString": "Server=crm-db;Database=crm;User Id=campaign_ro;Password=secret;",
  "description": "Main CRM customer table"
}
```

The connection string is **encrypted at rest** using ASP.NET Core Data Protection.
The value you provide is never stored in plaintext.

### Recommended connection string format

```
Server=myserver;Database=CampaignData;
User Id=campaign_ro;Password=MyStr0ngPass!;
Connect Timeout=30;
TrustServerCertificate=False;Encrypt=True;
Application Name=CampaignEngine;
Pooling=True;Min Pool Size=0;Max Pool Size=100;
```

> **Security note:** The database account should have **SELECT-only** permission on the
> target tables. CampaignEngine connectors never issue INSERT, UPDATE, DELETE, or DDL.

---

## Targeting a Specific Table

By default the schema discovery returns all user tables in the database. To target a
specific table, append `Table=TableName;` to the connection string:

```
Server=myserver;Database=crm;User Id=reader;Password=secret;Table=dbo.Customers;
```

When a table name is specified:

- `GetSchemaAsync` queries `INFORMATION_SCHEMA.COLUMNS` for that table only.
- `QueryAsync` generates `SELECT [col1], [col2] FROM [dbo.Customers] WHERE ...`
- Column names from the declared schema are quoted with `[brackets]` to prevent injection.

---

## Schema Discovery

### Automatic discovery

Once a data source is saved, click **Discover Schema** in the data source Details page
(or call `PUT /api/datasources/{id}/schema` with an empty body). The connector will:

1. Open a connection to SQL Server.
2. Query `INFORMATION_SCHEMA.COLUMNS` for the configured table (or all base tables).
3. Map SQL Server data types to canonical `FieldDefinitionDto.FieldType` values.
4. Return the field list for the Admin to review and save.

### SQL type mapping

| SQL Server type | Canonical type |
|-----------------|----------------|
| `nvarchar`, `varchar`, `char`, `nchar`, `text`, `ntext` | `nvarchar` |
| `int` | `int` |
| `bigint` | `bigint` |
| `smallint`, `tinyint` | `int` |
| `datetime`, `datetime2`, `smalldatetime` | `datetime` |
| `date` | `date` |
| `time` | `time` |
| `bit` | `bit` |
| `decimal`, `numeric`, `money`, `smallmoney` | `decimal` |
| `float`, `real` | `float` |
| `uniqueidentifier` | `uniqueidentifier` |
| `binary`, `varbinary`, `image` | `varbinary` |

Binary and LOB types (`varbinary`, `text`, `ntext`, `image`) are marked
`isFilterable = false` by default.

---

## Parameterized Query Generation

### Security model

The query builder strictly enforces parameterization:

1. **Values** — all filter values are added as `DynamicParameters`. The value string
   `'; DROP TABLE Recipients; --'` becomes a parameter, not SQL text.

2. **Column names** — validated against the declared schema before use.
   A column name not in the schema raises `InvalidOperationException`.
   Validated column names are quoted with `[name]` syntax.

3. **Operators** — whitelisted to prevent injection via the operator field.
   Only the following operators are accepted:

   | Operator | Description |
   |----------|-------------|
   | `=` | Equality |
   | `!=` / `<>` | Inequality |
   | `>` | Greater than |
   | `<` | Less than |
   | `>=` | Greater than or equal |
   | `<=` | Less than or equal |
   | `LIKE` | Pattern match |
   | `NOT LIKE` | Negative pattern match |
   | `IN` | Membership in a list |
   | `IS NULL` | Null check |
   | `IS NOT NULL` | Non-null check |

   Any other operator string (e.g., `EXEC`, `UNION`, `DROP`) raises
   `InvalidOperationException` before any SQL is executed.

4. **Table names** — extracted from the connection string `Table=` keyword.
   Validated to contain only alphanumeric characters, underscores, hyphens, spaces,
   and dots (for schema.table notation). Wrapped in `[brackets]`.

### Generated SQL examples

Filter `Age >= 18 AND Status = 'active'`:

```sql
SELECT [Id], [Email], [Age], [Status]
FROM [Customers]
WHERE ([Age] >= @p0 AND [Status] = @p1)
```

Parameters: `@p0 = 18`, `@p1 = 'active'`

Filter `Email LIKE '%@example.com'`:

```sql
SELECT [Id], [Email]
FROM [Customers]
WHERE [Email] LIKE @p0
```

Parameter: `@p0 = '%@example.com'`

Filter `Category IN ('A', 'B', 'C')`:

```sql
SELECT [Id], [Email], [Category]
FROM [Customers]
WHERE [Category] IN (@p0_0, @p0_1, @p0_2)
```

Parameters: `@p0_0 = 'A'`, `@p0_1 = 'B'`, `@p0_2 = 'C'`

---

## Filter Expression DSL

Filters are passed as a `FilterExpressionDto` tree (AST).

### Leaf node

```json
{
  "fieldName": "Age",
  "operator": ">=",
  "value": 18
}
```

### Composite node (AND / OR)

```json
{
  "logicalOperator": "AND",
  "children": [
    { "fieldName": "Age",    "operator": ">=", "value": 18 },
    { "fieldName": "Status", "operator": "=",  "value": "active" }
  ]
}
```

### Constraints

- Maximum nesting depth: **5 levels**.
- `IN` operator: pass value as a JSON array — e.g., `"value": ["A", "B", "C"]`.
- `IS NULL` / `IS NOT NULL` operators: value is ignored.
- Empty `IN` list generates `1=0` (no rows match).

---

## Connection Pooling

ADO.NET connection pooling is enabled automatically when `Pooling=True` is in the
connection string (the default). The connector appends pool settings from
`SqlServerConnectorOptions` to every connection string at runtime.

### Pool lifecycle

```
Campaign Operator requests batch
  → Hangfire worker acquires connection from pool
  → Executes SELECT with 30s timeout
  → Returns connection to pool
  → Pool keeps connection alive for reuse
```

For large batch campaigns with 8 concurrent Hangfire workers, ensure:

```json
"MaxPoolSize": 200
```

Each worker may open multiple connections per chunk if the data source is queried
multiple times. `MaxPoolSize` should be at least `WorkerCount × ConnectionsPerChunk`.

---

## Troubleshooting

| Symptom | Likely cause | Resolution |
|---------|-------------|------------|
| `Connection failed: A network-related or instance-specific error` | SQL Server hostname not reachable | Verify the server name; check firewall rules between CampaignEngine server and SQL Server |
| `Connection failed: Login failed for user 'campaign_ro'` | Wrong credentials in connection string | Update the connection string with the correct User Id and Password |
| `Connection failed: Cannot open database 'crm'` | Database does not exist or user lacks permission | Grant `CONNECT` and `SELECT` permissions on the target database |
| `Query timeout expired` | Query takes longer than 30 seconds | Add indexes on filtered columns; consider pre-filtering the table with a VIEW |
| `Filter field 'X' is not declared in the data source schema` | Field used in campaign filter not in declared schema | Re-run schema discovery or manually add the field in the data source Edit Schema page |
| `SQL operator 'EXEC' is not supported` | Attempted operator injection blocked | This is expected security behaviour — use only whitelisted operators |
| `InvalidOperationException: Identifier contains invalid characters` | Table name in connection string has unsafe characters | Use only alphanumeric, underscore, or dot characters in the table name |

---

## Adding New Connector Types

The `IDataSourceConnector` interface is the extension point for new data source types.

```csharp
public class RestApiConnector : IDataSourceConnector
{
    public Task<IReadOnlyList<IDictionary<string, object?>>> QueryAsync(
        DataSourceDefinitionDto definition,
        IReadOnlyList<FilterExpressionDto>? filters,
        CancellationToken cancellationToken = default) { ... }

    public Task<IReadOnlyList<FieldDefinitionDto>> GetSchemaAsync(
        DataSourceDefinitionDto definition,
        CancellationToken cancellationToken = default) { ... }
}
```

Register in `ServiceCollectionExtensions.cs`:

```csharp
services.AddScoped<IDataSourceConnector, RestApiConnector>();
```

A future connector registry (keyed by `DataSourceType`) will resolve the correct
implementation at runtime when multiple connectors are registered.

---

## Business Rules

1. **Parameterized SQL only** — all filter values are bound as parameters. No string concatenation of values into SQL.
2. **Read-only** — only `SELECT` queries are executed. The database account must have read-only access.
3. **Query timeout: 30 seconds** — configurable per connection, hard-coded in the connector per the US-015 specification.
4. **Connection pooling** — enabled by default via ADO.NET; pool size configurable via `appsettings.json`.
5. **Schema validation** — filter field names are validated against the declared schema. Unknown fields are rejected.
6. **Operator whitelist** — only safe, approved SQL operators are allowed in filter expressions.
