# Database Schema and Migration Strategy

## Overview

CampaignEngine uses **SQL Server** as the primary database with **Entity Framework Core 8** for schema management and data access. All entity configurations follow the code-first approach using `IEntityTypeConfiguration<T>` classes.

## Connection String Configuration

Connection strings are configured in `appsettings.json` under the `ConnectionStrings` section:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(local);Database=CampaignEngine;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
  }
}
```

For production, use environment-specific overrides (`appsettings.Production.json`) or environment variables.

## Connection String Encryption for Data Sources

External data source connection strings are **encrypted at rest** using ASP.NET Core Data Protection:

- Interface: `IConnectionStringEncryptor` (`CampaignEngine.Infrastructure.Persistence.Security`)
- Implementation: `DataProtectionConnectionStringEncryptor` using `IDataProtector`
- Purpose string: `CampaignEngine.DataSource.ConnectionString`
- On Windows/IIS: keys stored in the local file system (DPAPI-protected)

**Usage pattern:**
```csharp
// Encrypt before saving to DB
dataSource.EncryptedConnectionString = encryptor.Encrypt(plaintextConnectionString);

// Decrypt when reading for use
var connectionString = encryptor.Decrypt(dataSource.EncryptedConnectionString);
```

## Database Schema

### Core Tables

| Table | Entity | Soft Delete | Description |
|-------|--------|-------------|-------------|
| `Templates` | `Template` | Yes | Message templates with HTML body and channel |
| `PlaceholderManifests` | `PlaceholderManifestEntry` | No | Typed placeholder declarations per template |
| `TemplateHistory` | `TemplateHistory` | No | Immutable version snapshots for audit |
| `TemplateSnapshots` | `TemplateSnapshot` | No | Frozen template content at campaign scheduling |
| `DataSources` | `DataSource` | No | External data repository declarations |
| `DataSourceFields` | `DataSourceField` | No | Schema field definitions for data sources |
| `Campaigns` | `Campaign` | Yes | Campaign configurations and progress |
| `CampaignSteps` | `CampaignStep` | No | Ordered steps in multi-step campaigns |
| `CampaignAttachments` | `CampaignAttachment` | No | Static and dynamic file attachments |
| `SendLogs` | `SendLog` | No | Every send attempt (source of truth) |
| `ApiKeys` | `ApiKey` | No | API key authentication records |

### Business Rules Reflected in Schema

1. **All IDs use GUID** — set as default values, suitable for distributed generation
2. **Audit fields on all entities** — `CreatedAt` and `UpdatedAt` (auto-set by `CampaignEngineDbContext.SaveChanges`)
3. **Soft delete** on `Templates` and `Campaigns` — `IsDeleted` flag with `DeletedAt` timestamp; global query filters applied automatically
4. **Encrypted connection strings** — `DataSource.EncryptedConnectionString` stores only ciphertext

### Key Indexes

- `Templates`: unique `(Name, Channel)` filtered by `IsDeleted = 0`
- `Campaigns`: unique `Name` filtered by `IsDeleted = 0`
- `DataSources`: unique `Name`
- `PlaceholderManifests`: unique `(TemplateId, Key)`
- `DataSourceFields`: unique `(DataSourceId, FieldName)`
- `ApiKeys`: unique `Name`
- `SendLogs`: indexed on `CampaignId`, `Status`, `CreatedAt`, `CorrelationId`

## Migration Strategy

### Running Migrations

```bash
# Add a new migration
dotnet ef migrations add <MigrationName> \
  --project src/CampaignEngine.Infrastructure \
  --startup-project src/CampaignEngine.Infrastructure \
  --output-dir Persistence/Migrations

# Apply migrations to database
dotnet ef database update \
  --project src/CampaignEngine.Infrastructure \
  --startup-project src/CampaignEngine.Infrastructure
```

> The `CampaignEngineDbContextFactory` provides design-time context creation without requiring the Web project.

### Applying Migrations at Runtime

In development, migrations are applied automatically during database seeding:

```csharp
// Program.cs (Development only)
if (app.Environment.IsDevelopment())
    await app.SeedDatabaseAsync();
```

In production, migrations should be applied as part of the deployment pipeline:

```bash
dotnet ef database update --environment Production
```

Or using the EF Core Bundle approach for zero-downtime deployments.

### Migration File Location

All migration files are stored in:
```
src/CampaignEngine.Infrastructure/Persistence/Migrations/
```

### Current Migrations

| Migration | Date | Description |
|-----------|------|-------------|
| `20260319170157_InitialCreate` | 2026-03-19 | Creates all core tables with indexes and constraints |

## Development Seed Data

The `DatabaseSeeder` service populates the development database with representative data:

| Entity | Seeded Records |
|--------|---------------|
| Templates | 3 (Welcome Email - Published, Appointment SMS - Published, Newsletter - Draft) |
| DataSources | 1 (Sample Customer Database with 5 fields) |

**Seed data is idempotent** — running it multiple times will not create duplicates.

### Triggering Seed

Register in `Program.cs`:

```csharp
if (app.Environment.IsDevelopment())
{
    await app.SeedDatabaseAsync();
}
```

The `DatabaseSeeder` class is registered automatically via `services.AddInfrastructure()`.

## DbContext Configuration Details

The `CampaignEngineDbContext` provides:

- SQL Server retry policy: 3 attempts, 30-second delay
- Command timeout: 30 seconds
- Connection resiliency for transient failures
- Automatic `UpdatedAt` timestamp management in `SaveChanges`/`SaveChangesAsync`
- All entity configurations applied via `ApplyConfigurationsFromAssembly`

### Global Query Filters

Soft-deleted entities are automatically excluded from queries:

```csharp
// Returns only non-deleted templates
var templates = await context.Templates.ToListAsync();

// Include soft-deleted records explicitly
var allTemplates = await context.Templates.IgnoreQueryFilters().ToListAsync();
```

## Testing Strategy

Integration tests use the **EF Core In-Memory provider** to avoid SQL Server dependency in CI:

```csharp
var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
    .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
    .Options;
var context = new CampaignEngineDbContext(options);
```

Test classes inherit from `DbContextTestBase` (in `CampaignEngine.Infrastructure.Tests`) which provides a fresh isolated database per test.

> **Note:** The in-memory provider does not support all SQL Server features (computed columns, raw SQL, etc.). For full SQL Server validation, use `TestContainers` or a dedicated test database.
